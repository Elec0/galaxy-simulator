use crate::{IdAllocationError, IdSequence, LocationId, RouteId, SimulationDuration};
use std::collections::{BTreeMap, BTreeSet};
use std::error::Error;
use std::fmt::{self, Display, Formatter};

/// Read-only navigation behavior used by ships and logistics systems.
pub trait Navigation {
    /// Returns a directed route by its stable ID.
    fn route(&self, route_id: RouteId) -> Option<DirectedRoute>;

    /// Finds the enabled path with the lowest total base travel duration.
    ///
    /// Equal-duration paths are ordered lexicographically by their route IDs.
    /// An origin equal to the destination produces an empty route plan.
    ///
    /// # Errors
    ///
    /// Returns [`NavigationError::UnknownLocation`] when either endpoint is not
    /// present, or [`NavigationError::DurationOverflow`] when a candidate path
    /// cannot be represented.
    fn find_route(
        &self,
        origin: LocationId,
        destination: LocationId,
    ) -> Result<Option<RoutePlan>, NavigationError>;
}

/// One directed connection between locations.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct DirectedRoute {
    id: RouteId,
    origin: LocationId,
    destination: LocationId,
    base_duration: SimulationDuration,
    enabled: bool,
}

impl DirectedRoute {
    /// Returns the stable route identifier.
    #[must_use]
    pub const fn id(self) -> RouteId {
        self.id
    }

    /// Returns the route's origin.
    #[must_use]
    pub const fn origin(self) -> LocationId {
        self.origin
    }

    /// Returns the route's destination.
    #[must_use]
    pub const fn destination(self) -> LocationId {
        self.destination
    }

    /// Returns the travel duration used by Phase 1 ships.
    #[must_use]
    pub const fn base_duration(self) -> SimulationDuration {
        self.base_duration
    }

    /// Returns whether new path searches may use this route.
    #[must_use]
    pub const fn is_enabled(self) -> bool {
        self.enabled
    }
}

/// Deterministic route returned by a navigation query.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct RoutePlan {
    route_ids: Vec<RouteId>,
    total_duration: SimulationDuration,
}

impl RoutePlan {
    /// Returns the ordered directed routes to travel.
    #[must_use]
    pub fn route_ids(&self) -> &[RouteId] {
        &self.route_ids
    }

    /// Returns the sum of route base durations.
    #[must_use]
    pub const fn total_duration(&self) -> SimulationDuration {
        self.total_duration
    }
}

#[derive(Clone, Debug, Eq, PartialEq, Ord, PartialOrd)]
struct PathCandidate {
    total_duration: SimulationDuration,
    route_ids: Vec<RouteId>,
    location: LocationId,
}

/// Deterministic directed multigraph used by the Phase 1 navigation backend.
#[derive(Clone, Debug, Default)]
pub struct RouteGraph {
    locations: BTreeSet<LocationId>,
    routes: BTreeMap<RouteId, DirectedRoute>,
    outgoing: BTreeMap<LocationId, BTreeSet<RouteId>>,
    route_ids: IdSequence<RouteId>,
}

impl RouteGraph {
    /// Creates an empty route graph.
    #[must_use]
    pub const fn new() -> Self {
        Self {
            locations: BTreeSet::new(),
            routes: BTreeMap::new(),
            outgoing: BTreeMap::new(),
            route_ids: IdSequence::new(),
        }
    }

    /// Adds a known location and returns whether it was newly inserted.
    pub fn add_location(&mut self, location: LocationId) -> bool {
        let inserted = self.locations.insert(location);
        self.outgoing.entry(location).or_default();
        inserted
    }

    /// Adds one directed route and allocates its stable ID.
    ///
    /// Parallel routes between the same endpoints are allowed.
    ///
    /// # Errors
    ///
    /// Returns [`NavigationError::UnknownLocation`] when either endpoint is not
    /// present, or [`NavigationError::IdAllocation`] if route IDs are
    /// exhausted.
    pub fn add_route(
        &mut self,
        origin: LocationId,
        destination: LocationId,
        base_duration: SimulationDuration,
    ) -> Result<RouteId, NavigationError> {
        self.require_location(origin)?;
        self.require_location(destination)?;

        let id = self.route_ids.allocate()?;
        let route = DirectedRoute {
            id,
            origin,
            destination,
            base_duration,
            enabled: true,
        };
        self.routes.insert(id, route);
        self.outgoing.entry(origin).or_default().insert(id);
        Ok(id)
    }

    /// Adds two directed routes with the same duration.
    ///
    /// The first returned route travels from `first` to `second`; the second
    /// travels from `second` to `first`.
    ///
    /// # Errors
    ///
    /// Returns the same errors as [`Self::add_route`].
    pub fn add_bidirectional_routes(
        &mut self,
        first: LocationId,
        second: LocationId,
        base_duration: SimulationDuration,
    ) -> Result<(RouteId, RouteId), NavigationError> {
        let forward = self.add_route(first, second, base_duration)?;
        let reverse = self.add_route(second, first, base_duration)?;
        Ok((forward, reverse))
    }

    /// Returns a route by its stable ID.
    #[must_use]
    pub fn route(&self, route_id: RouteId) -> Option<DirectedRoute> {
        self.routes.get(&route_id).copied()
    }

    /// Enables or disables a route for future path searches.
    ///
    /// # Errors
    ///
    /// Returns [`NavigationError::UnknownRoute`] if `route_id` is not present.
    pub fn set_route_enabled(
        &mut self,
        route_id: RouteId,
        enabled: bool,
    ) -> Result<(), NavigationError> {
        let route = self
            .routes
            .get_mut(&route_id)
            .ok_or(NavigationError::UnknownRoute(route_id))?;
        route.enabled = enabled;
        Ok(())
    }

    fn require_location(&self, location: LocationId) -> Result<(), NavigationError> {
        if self.locations.contains(&location) {
            Ok(())
        } else {
            Err(NavigationError::UnknownLocation(location))
        }
    }
}

impl Navigation for RouteGraph {
    fn route(&self, route_id: RouteId) -> Option<DirectedRoute> {
        self.route(route_id)
    }

    fn find_route(
        &self,
        origin: LocationId,
        destination: LocationId,
    ) -> Result<Option<RoutePlan>, NavigationError> {
        self.require_location(origin)?;
        self.require_location(destination)?;

        if origin == destination {
            return Ok(Some(RoutePlan {
                route_ids: Vec::new(),
                total_duration: SimulationDuration::ZERO,
            }));
        }

        let initial = PathCandidate {
            total_duration: SimulationDuration::ZERO,
            route_ids: Vec::new(),
            location: origin,
        };
        let mut best = BTreeMap::from([(origin, initial.clone())]);
        let mut frontier = BTreeSet::from([initial]);

        while let Some(candidate) = frontier.pop_first() {
            if best.get(&candidate.location) != Some(&candidate) {
                continue;
            }

            if candidate.location == destination {
                return Ok(Some(RoutePlan {
                    route_ids: candidate.route_ids,
                    total_duration: candidate.total_duration,
                }));
            }

            let Some(route_ids) = self.outgoing.get(&candidate.location) else {
                continue;
            };

            for route_id in route_ids {
                let Some(route) = self.routes.get(route_id).copied() else {
                    continue;
                };
                if !route.enabled {
                    continue;
                }

                let total_duration = candidate
                    .total_duration
                    .checked_add(route.base_duration)
                    .map_err(|_| NavigationError::DurationOverflow)?;
                let mut path = candidate.route_ids.clone();
                path.push(route.id);
                let next = PathCandidate {
                    total_duration,
                    route_ids: path,
                    location: route.destination,
                };

                if best.get(&next.location).is_none_or(|known| next < *known) {
                    if let Some(previous) = best.insert(next.location, next.clone()) {
                        frontier.remove(&previous);
                    }
                    frontier.insert(next);
                }
            }
        }

        Ok(None)
    }
}

/// Errors produced by graph mutation and route searches.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum NavigationError {
    /// A referenced location is not present in the graph.
    UnknownLocation(LocationId),
    /// A referenced route is not present in the graph.
    UnknownRoute(RouteId),
    /// A route ID could not be allocated.
    IdAllocation(IdAllocationError),
    /// Adding route durations exceeded the integer timeline.
    DurationOverflow,
}

impl From<IdAllocationError> for NavigationError {
    fn from(error: IdAllocationError) -> Self {
        Self::IdAllocation(error)
    }
}

impl Display for NavigationError {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> fmt::Result {
        match self {
            Self::UnknownLocation(location) => write!(formatter, "unknown location {location}"),
            Self::UnknownRoute(route) => write!(formatter, "unknown route {route}"),
            Self::IdAllocation(error) => Display::fmt(error, formatter),
            Self::DurationOverflow => formatter.write_str("route duration overflow"),
        }
    }
}

impl Error for NavigationError {}

#[cfg(test)]
mod tests {
    use super::{Navigation, RouteGraph};
    use crate::{IdSequence, LocationId, SimulationDuration};

    fn locations<const N: usize>() -> [LocationId; N] {
        let mut ids = IdSequence::new();
        std::array::from_fn(|_| ids.allocate().expect("fixture ID should exist"))
    }

    #[test]
    fn bidirectional_helper_creates_distinct_directed_routes() {
        let [first, second] = locations();
        let mut graph = RouteGraph::new();
        graph.add_location(first);
        graph.add_location(second);

        let (forward, reverse) = graph
            .add_bidirectional_routes(first, second, SimulationDuration::from_millis(10))
            .expect("fixture routes should be valid");

        assert_ne!(forward, reverse);
        assert_eq!(graph.route(forward).expect("forward route").origin(), first);
        assert_eq!(
            graph.route(reverse).expect("reverse route").origin(),
            second
        );
    }

    #[test]
    fn shortest_enabled_path_wins() {
        let [first, second, third] = locations();
        let mut graph = RouteGraph::new();
        for location in [first, second, third] {
            graph.add_location(location);
        }
        let direct = graph
            .add_route(first, third, SimulationDuration::from_millis(30))
            .expect("direct route should be valid");
        let first_leg = graph
            .add_route(first, second, SimulationDuration::from_millis(10))
            .expect("first leg should be valid");
        let second_leg = graph
            .add_route(second, third, SimulationDuration::from_millis(10))
            .expect("second leg should be valid");

        let plan = graph
            .find_route(first, third)
            .expect("path search should succeed")
            .expect("path should exist");

        assert_eq!(plan.route_ids(), &[first_leg, second_leg]);
        assert_eq!(plan.total_duration(), SimulationDuration::from_millis(20));
        assert!(!plan.route_ids().contains(&direct));
    }

    #[test]
    fn disabled_route_is_excluded_from_new_plans() {
        let [first, second, third] = locations();
        let mut graph = RouteGraph::new();
        for location in [first, second, third] {
            graph.add_location(location);
        }
        let direct = graph
            .add_route(first, third, SimulationDuration::from_millis(10))
            .expect("direct route should be valid");
        let first_leg = graph
            .add_route(first, second, SimulationDuration::from_millis(10))
            .expect("first leg should be valid");
        let second_leg = graph
            .add_route(second, third, SimulationDuration::from_millis(10))
            .expect("second leg should be valid");
        graph
            .set_route_enabled(direct, false)
            .expect("route should exist");

        let plan = graph
            .find_route(first, third)
            .expect("path search should succeed")
            .expect("alternate path should exist");

        assert_eq!(plan.route_ids(), &[first_leg, second_leg]);
    }

    #[test]
    fn equal_duration_paths_use_route_id_order() {
        let [first, second, third, fourth] = locations();
        let mut graph = RouteGraph::new();
        for location in [first, second, third, fourth] {
            graph.add_location(location);
        }
        let preferred_first = graph
            .add_route(first, second, SimulationDuration::from_millis(10))
            .expect("route should be valid");
        let preferred_second = graph
            .add_route(second, fourth, SimulationDuration::from_millis(10))
            .expect("route should be valid");
        graph
            .add_route(first, third, SimulationDuration::from_millis(10))
            .expect("route should be valid");
        graph
            .add_route(third, fourth, SimulationDuration::from_millis(10))
            .expect("route should be valid");

        let plan = graph
            .find_route(first, fourth)
            .expect("path search should succeed")
            .expect("path should exist");

        assert_eq!(plan.route_ids(), &[preferred_first, preferred_second]);
    }
}
