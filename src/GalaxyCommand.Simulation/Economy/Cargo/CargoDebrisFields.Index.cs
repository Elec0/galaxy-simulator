namespace GalaxyCommand.Simulation;

/// <summary>
/// Derived per-system circle interval trees. Augmented closed X bounds prune
/// distant circles without enumerating empty space or comparing every field.
/// Traversal order is never a merge or identity tie-breaker.
/// </summary>
internal sealed class CargoDebrisCircleIndex
{
    private sealed class Node(CargoDebrisCircle circle)
    {
        internal CargoDebrisCircle Circle = circle;
        internal Node? Left;
        internal Node? Right;
        internal int Height = 1;
        internal long MaxX = MaximumX(circle);
    }

    private readonly Dictionary<SystemId, Node?> _roots = [];

    /// <summary>Adds one unique validated occurrence circle at owner commit.</summary>
    internal void Add(CargoDebrisCircle circle)
    {
        _roots.TryGetValue(circle.Center.SystemId, out Node? root);
        _roots[circle.Center.SystemId] = Insert(root, circle);
    }

    /// <summary>Removes one retired field's occurrence circle from the derived index.</summary>
    internal void Remove(CargoDebrisCircle circle)
    {
        _roots.TryGetValue(circle.Center.SystemId, out Node? root);
        Node? next = Delete(root, circle);
        if (next is null) _roots.Remove(circle.Center.SystemId);
        else _roots[circle.Center.SystemId] = next;
    }

    /// <summary>Returns conservative same-system box candidates without changing index state.</summary>
    internal IReadOnlyList<CargoDebrisCircle> Query(CargoDebrisCircle query)
    {
        var result = new List<CargoDebrisCircle>();
        if (_roots.TryGetValue(query.Center.SystemId, out Node? root))
            Visit(root, query, result);
        return result;
    }

    /// <summary>Prunes subtrees whose maximum extent or ordered minimum cannot touch the query.</summary>
    private static void Visit(Node? node, CargoDebrisCircle query, List<CargoDebrisCircle> result)
    {
        if (node is null || node.MaxX < MinimumX(query)) return;
        Visit(node.Left, query, result);
        if (MinimumX(node.Circle) > MaximumX(query)) return;
        if (MaximumX(node.Circle) >= MinimumX(query)
            && MaximumY(node.Circle) >= MinimumY(query) && MinimumY(node.Circle) <= MaximumY(query))
            result.Add(node.Circle);
        Visit(node.Right, query, result);
    }

    /// <summary>Maintains deterministic ordering and AVL balance for a newly committed circle.</summary>
    private static Node Insert(Node? node, CargoDebrisCircle circle)
    {
        if (node is null) return new Node(circle);
        int compare = Compare(circle, node.Circle);
        if (compare < 0) node.Left = Insert(node.Left, circle);
        else if (compare > 0) node.Right = Insert(node.Right, circle);
        else throw new InvalidOperationException("A debris occurrence circle is already indexed.");
        return Balance(node);
    }

    /// <summary>Retires a known circle while preserving subtree interval summaries and balance.</summary>
    private static Node? Delete(Node? node, CargoDebrisCircle circle)
    {
        if (node is null) return null;
        int compare = Compare(circle, node.Circle);
        if (compare < 0) node.Left = Delete(node.Left, circle);
        else if (compare > 0) node.Right = Delete(node.Right, circle);
        else
        {
            if (node.Left is null) return node.Right;
            if (node.Right is null) return node.Left;
            Node successor = node.Right;
            while (successor.Left is not null) successor = successor.Left;
            node.Circle = successor.Circle;
            node.Right = Delete(node.Right, successor.Circle);
        }
        return Balance(node);
    }

    /// <summary>Rebalances only derived structure; circle identities and geometry remain unchanged.</summary>
    private static Node Balance(Node node)
    {
        Update(node);
        int difference = Height(node.Left) - Height(node.Right);
        if (difference > 1)
        {
            if (Height(node.Left!.Left) < Height(node.Left.Right)) node.Left = RotateLeft(node.Left);
            return RotateRight(node);
        }
        if (difference < -1)
        {
            if (Height(node.Right!.Right) < Height(node.Right.Left)) node.Right = RotateRight(node.Right);
            return RotateLeft(node);
        }
        return node;
    }

    private static Node RotateLeft(Node node)
    {
        Node promoted = node.Right!;
        node.Right = promoted.Left;
        promoted.Left = node;
        Update(node);
        Update(promoted);
        return promoted;
    }

    private static Node RotateRight(Node node)
    {
        Node promoted = node.Left!;
        node.Left = promoted.Right;
        promoted.Right = node;
        Update(node);
        Update(promoted);
        return promoted;
    }

    /// <summary>Recomputes the conservative extent after a structural change.</summary>
    private static void Update(Node node)
    {
        node.Height = Math.Max(Height(node.Left), Height(node.Right)) + 1;
        node.MaxX = Math.Max(MaximumX(node.Circle), Math.Max(node.Left?.MaxX ?? long.MinValue, node.Right?.MaxX ?? long.MinValue));
    }

    private static int Height(Node? node) => node?.Height ?? 0;

    /// <summary>Uses original occurrence identity to keep equal interval starts distinct.</summary>
    private static int Compare(CargoDebrisCircle left, CargoDebrisCircle right)
    {
        int minimum = MinimumX(left).CompareTo(MinimumX(right));
        return minimum != 0 ? minimum : left.OriginalFieldId.Value.CompareTo(right.OriginalFieldId.Value);
    }

    private static long MinimumX(CargoDebrisCircle circle) => checked(circle.Center.Position.X.Units - (long)circle.RadiusMeters);
    private static long MaximumX(CargoDebrisCircle circle) => checked(circle.Center.Position.X.Units + (long)circle.RadiusMeters);
    private static long MinimumY(CargoDebrisCircle circle) => checked(circle.Center.Position.Y.Units - (long)circle.RadiusMeters);
    private static long MaximumY(CargoDebrisCircle circle) => checked(circle.Center.Position.Y.Units + (long)circle.RadiusMeters);
}
