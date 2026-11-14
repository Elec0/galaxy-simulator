#!/usr/bin/env python3
"""Runs native dialogue interactions using temporary copied-content fixtures."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile


def text(key, fallback, arguments=None, rich=False):
    return {"resourceKey": "smoke.dialogue." + key, "invariantFallback": fallback,
            "markup": "restricted-rich-text" if rich else "plain-text", "arguments": arguments or []}


def fixture_definitions():
    """Builds explicit test content without modifying the authored preview package."""
    def segment(identity, value):
        return {"id": identity, "order": 0, "text": value,
                "speaker": text("speaker", "Captain Vale")}

    def choice(identity, label, transition, move=False):
        result = {"id": identity, "order": 0, "label": text(identity, label), "transition": transition,
                  "repeatability": "repeatable", "offerConditions": [], "admissionConditions": [], "memoryIds": []}
        if move:
            result["move"] = {"kind": "move-ship", "actorRoleId": "actor", "destinationRoleId": "target", "placement": "replace-all"}
        return result

    response = {"kind": "dialogue", "id": "smoke-response", "fallback": "Dialogue smoke response", "references": [],
                "values": {"roles": [{"id": "actor", "acceptedKinds": ["ship"]}, {"id": "target", "acceptedKinds": ["ship"]}],
                           "initialNodeId": "entry", "repeatability": "repeatable", "startConditions": [], "memories": [],
                           "nodes": [
                               {"id": "entry", "segments": [segment("opening", text("entry", "Set a [b]course[/b] for {name}.",
                                   [{"name": "name", "kind": "text", "value": "Pilot [b]Vale"}], rich=True))],
                                "choices": [choice("accept", "Set course", {"kind": "node", "nodeId": "confirm"}, move=True)]},
                               {"id": "confirm", "segments": [segment("confirmation", text("confirm", "Course request accepted."))],
                                "choices": [choice("finish", "Finish", {"kind": "complete", "outcomeId": "done"})]}]}}
    information = {"kind": "dialogue", "id": "smoke-information", "fallback": "Dialogue smoke information", "references": [],
                   "values": {"roles": [], "initialNodeId": "entry", "repeatability": "repeatable", "startConditions": [], "memories": [],
                              "nodes": [{"id": "entry", "segments": [segment("notice", text("notice", "Navigation channel open."))],
                                         "choices": [], "automaticTransition": {"kind": "complete", "outcomeId": "informed"}}]}}
    unavailable_choice = choice("unavailable", "Unavailable response", {"kind": "complete", "outcomeId": "done"})
    unavailable_choice["admissionConditions"] = [{"kind": "receipt", "memoryId": "heard", "scope": "session", "exists": True}]
    unavailable = {"kind": "dialogue", "id": "smoke-unavailable", "fallback": "Dialogue smoke unavailable", "references": [],
                   "values": {"roles": [], "initialNodeId": "entry", "repeatability": "repeatable", "startConditions": [],
                              "memories": [{"id": "heard", "scope": "session"}],
                              "nodes": [{"id": "entry", "segments": [segment("waiting", text("waiting", "Awaiting a new response."))],
                                         "choices": [unavailable_choice]}]}}
    return [response, information, unavailable]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", default="/Applications/Godot_mono.app/Contents/MacOS/Godot")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--output", type=Path)
    arguments = parser.parse_args()
    repository = Path(__file__).resolve().parents[2]
    project = repository / "src/GalaxyCommand.Godot"
    output = (arguments.output or Path(tempfile.mkdtemp(prefix="galaxy-dialogue-smoke-"))).resolve()
    output.mkdir(parents=True, exist_ok=True)
    if not arguments.no_build:
        subprocess.run([arguments.godot, "--headless", "--editor", "--path", str(project), "--build-solutions", "--quit"],
                       cwd=repository, check=True)
    copied_content = project / ".godot/mono/temp/bin/Debug/DevelopmentContent/galaxy-command.preview/definitions.json"
    original = copied_content.read_bytes()
    fixture = json.loads(original)
    smoke = fixture_definitions()
    existing = {(item["kind"], item["id"]) for item in fixture["definitions"]}
    if any((item["kind"], item["id"]) in existing for item in smoke):
        raise RuntimeError("The copied preview already contains smoke fixture identities.")
    fixture["definitions"].extend(smoke)
    try:
        # Only disposable build output is overlaid; the authored package is never changed.
        copied_content.write_text(json.dumps(fixture, ensure_ascii=False), encoding="utf-8")
        result = subprocess.run([arguments.godot, "--path", str(project), "--script",
                                 str(Path(__file__).with_name("DialogueInputBindings.gd")), "--", "--output-dir=" + str(output)],
                                cwd=repository, env=os.environ.copy(), text=True, capture_output=True, timeout=120)
        log = result.stdout + result.stderr
        (output / "native-smoke.log").write_text(log, encoding="utf-8")
        print(log)
        print("Dialogue smoke artifacts:", output)
        return result.returncode or (1 if "ERROR:" in log else 0)
    finally:
        copied_content.write_bytes(original)


if __name__ == "__main__":
    raise SystemExit(main())
