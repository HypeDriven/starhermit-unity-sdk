#!/usr/bin/env python3
"""Generate the API inventory and SDK coverage manifest.

The backend is the contract: every REST operation it exposes and every WebSocket protocol it
serves must be reachable from the SDK, or be explicitly classified. This script reads both sides
and emits the machine-readable manifest the coverage test enforces, so a new endpoint on the
server shows up as a failing test rather than as a gap nobody noticed.

Usage: python3 tools/generate_coverage.py [--backend PATH] [--check]
"""
import argparse
import json
import os
import re
import sys

VERBS = ("HttpGet", "HttpPost", "HttpPut", "HttpPatch", "HttpDelete", "HttpHead", "HttpOptions")
SDK_VERB_CALLS = {"Get": "GET", "Post": "POST", "Put": "PUT", "Patch": "PATCH", "Delete": "DELETE"}


def normalise(path: str) -> str:
    """Reduce a route to its shape so both sides compare equal: parameters lose their names."""
    path = path.replace("v{version:apiVersion}", "v1")
    path = re.sub(r"\{[^}]*\}", "{}", path)
    path = re.sub(r"/+", "/", path)
    return "/" + path.strip("/")


def read_backend(root: str):
    controllers = os.path.join(root, "src/Platform.Api/Controllers")
    operations = []
    for filename in sorted(os.listdir(controllers)):
        if not filename.endswith(".cs"):
            continue
        text = open(os.path.join(controllers, filename), encoding="utf-8").read()
        lines = text.split("\n")

        match = re.search(r'\[Route\("([^"]+)"\)\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:sealed\s+)?class', text)
        base = match.group(1) if match else ""

        pending = []
        for index, raw in enumerate(lines):
            line = raw.strip()
            verb_match = re.match(r'\[(%s)(?:\("([^"]*)"[^\]]*\))?\]' % "|".join(VERBS), line)
            if verb_match:
                pending.append((verb_match.group(1)[4:].upper(), verb_match.group(2) or ""))
                continue
            if line.startswith("["):
                continue
            signature = re.match(r'(?:public|internal)\s+(?:async\s+)?[\w<>\[\],\s\?\.]+?\s+(\w+)\s*\(', line)
            if signature and pending:
                for verb, template in pending:
                    if template.startswith("~/"):
                        route = template[1:]
                    else:
                        route = base + ("/" + template if template else "")
                    operations.append({
                        "operationId": f"{filename[:-len('Controller.cs')]}.{signature.group(1)}",
                        "method": verb,
                        "path": normalise(route),
                        "source": filename,
                    })
                pending = []
    return operations


def iter_method_bodies(text: str):
    """Yield each method body, block-bodied or expression-bodied, so a path and its operation id
    are always paired within the method that owns them."""
    for match in re.finditer(r"\n\s+(?:public|internal|private|protected)[^\n;=]*?\w+\s*\(", text):
        # Walk to the end of the parameter list.
        index, depth = match.end() - 1, 0
        while index < len(text):
            if text[index] == "(":
                depth += 1
            elif text[index] == ")":
                depth -= 1
                if depth == 0:
                    break
            index += 1
        index += 1

        while index < len(text) and text[index] in " \r\n\t":
            index += 1

        if text.startswith("=>", index):
            end = text.find(";", index)
            while end > 0 and text.count("(", index, end) != text.count(")", index, end):
                end = text.find(";", end + 1)
            if end > 0:
                yield text[index:end]
            continue

        if index < len(text) and text[index] == "{":
            depth, cursor = 0, index
            while cursor < len(text):
                if text[cursor] == "{":
                    depth += 1
                elif text[cursor] == "}":
                    depth -= 1
                    if depth == 0:
                        break
                cursor += 1
            yield text[index:cursor]


def read_sdk(root: str):
    runtime = os.path.join(root, "Packages/com.starhermit.sdk/Runtime")
    operations = []
    for directory, _, files in os.walk(runtime):
        for filename in sorted(files):
            if not filename.endswith(".cs"):
                continue
            text = open(os.path.join(directory, filename), encoding="utf-8").read()
            prefix = "games/{}/" if '_prefix = $"games/' in text else ""

            for body in iter_method_bodies(text):
                identifier = re.search(r'"([a-zA-Z]+\.[a-zA-Z]+)"', body)
                if identifier is None:
                    continue

                verb_match = re.search(r'\b(Get|Post|Put|Patch|Delete)\(\$?"([^"]*)"\)', body)
                if verb_match is not None:
                    path = verb_match.group(2).replace("{_prefix}", prefix.rstrip("/") or "{_prefix}")
                    operations.append({
                        "operationId": identifier.group(1),
                        "method": SDK_VERB_CALLS[verb_match.group(1)],
                        "path": normalise("api/v1/" + path),
                        "source": filename,
                    })
                    continue

                scoped = re.search(r'Request\("(GET|POST|PUT|PATCH|DELETE)",\s*(?:\$?"([^"]*)"|string\.Empty)\)', body)
                if scoped is not None:
                    operations.append({
                        "operationId": identifier.group(1),
                        "method": scoped.group(1),
                        "path": normalise("api/v1/" + prefix + (scoped.group(2) or "")),
                        "source": filename,
                    })

    unique = {}
    for operation in operations:
        unique.setdefault((operation["method"], operation["path"]), operation)
    return list(unique.values())


SOCKETS = [
    {"protocol": "chat", "path": "/ws/v1/chat", "sdk": "StarhermitChatConnection"},
    {"protocol": "voice", "path": "/ws/v1/voice", "sdk": "StarhermitVoiceConnection"},
    {"protocol": "games", "path": "/ws/v1/games", "sdk": "StarhermitGameConnection"},
    {"protocol": "realtime", "path": "/ws/v1/realtime", "sdk": "StarhermitRealtimeConnection"},
    {"protocol": "relay", "path": "/ws/v1/relay", "sdk": "StarhermitRelayConnection"},
    {"protocol": "game-upload", "path": "/ws/v1/game-upload", "sdk": "StarhermitGameUploadConnection"},
]

# Operations the SDK deliberately does not type, with the reason. Everything here stays reachable
# through client.Raw, and the coverage test requires a reason rather than silence.
CLASSIFIED = {
    ("POST", "/api/v1/webhooks/email/resend"):
        "Server-to-server: the deployment's email provider posts delivery events here. A game client has no part in it.",
    ("GET", "/api/v1/game-host/{}/fallback/{}"):
        "Served to the browser that hosts a platform-hosted game, not called by a game client.",
    ("GET", "/api/v1/auth/oauth/{}/callback"):
        "Followed by the browser during OAuth, never called by the SDK; its result arrives through IStarhermitOAuthBrowser.",
    ("GET", "/api/v1/auth/oauth/{}/authorize"):
        "Opened in a browser rather than called: the SDK builds this URL with Auth.BuildAuthorizeUri.",
    ("GET", "/api/v1/github-games/{}/icon"):
        "Mapped as BrowserGames.GetIconAsync; also served directly to browsers.",
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--backend", default=os.path.expanduser("~/pi/dashboard/projects/starhermit"))
    parser.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    parser.add_argument("--check", action="store_true", help="fail when an operation is unmapped")
    args = parser.parse_args()

    backend = read_backend(args.backend)
    sdk = read_sdk(args.root)
    sdk_index = {(operation["method"], operation["path"]): operation for operation in sdk}

    mapped, unmapped, classified, socket_routes = [], [], [], []
    for operation in backend:
        if operation["path"].startswith("/ws/"):
            protocol = operation["path"].rsplit("/", 1)[-1]
            connection = next((s["sdk"] for s in SOCKETS if s["protocol"] == protocol), None)
            socket_routes.append({**operation, "connection": connection})
            if connection is None:
                unmapped.append(operation)
            continue

        key = (operation["method"], operation["path"])
        if key in sdk_index:
            mapped.append({**operation, "sdkOperation": sdk_index[key]["operationId"]})
        elif key in CLASSIFIED:
            classified.append({**operation, "classification": CLASSIFIED[key]})
        else:
            unmapped.append(operation)

    manifest = {
        "apiVersion": "v1",
        "generatedFrom": "Platform.Api controllers",
        "operationCount": len(backend),
        "mappedCount": len(mapped),
        "classifiedCount": len(classified),
        "operations": sorted(mapped, key=lambda o: (o["path"], o["method"])),
        "classified": sorted(classified, key=lambda o: (o["path"], o["method"])),
        "unmapped": sorted(unmapped, key=lambda o: (o["path"], o["method"])),
        "sockets": SOCKETS,
        "socketRoutes": socket_routes,
    }

    contracts = os.path.join(args.root, "contracts")
    os.makedirs(contracts, exist_ok=True)
    with open(os.path.join(contracts, "coverage-manifest.json"), "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")

    with open(os.path.join(contracts, "api-v1-operations.json"), "w", encoding="utf-8") as handle:
        json.dump({"apiVersion": "v1", "operations": backend, "sockets": SOCKETS}, handle, indent=2)
        handle.write("\n")

    docs = os.path.join(args.root, "Packages/com.starhermit.sdk/Documentation~")
    os.makedirs(docs, exist_ok=True)
    with open(os.path.join(docs, "api-coverage.md"), "w", encoding="utf-8") as handle:
        handle.write(render_markdown(manifest))

    generated = os.path.join(args.root, "Packages/com.starhermit.sdk/Tests/Runtime/Generated")
    os.makedirs(generated, exist_ok=True)
    with open(os.path.join(generated, "ApiCoverage.g.cs"), "w", encoding="utf-8") as handle:
        handle.write(render_csharp(manifest))

    print(f"{len(backend)} operations: {len(mapped)} REST mapped, {len(socket_routes)} socket routes, {len(classified)} classified, {len(unmapped)} unmapped")
    for operation in unmapped:
        print(f"  UNMAPPED {operation['method']:6s} {operation['path']}  ({operation['operationId']})")

    return 1 if (args.check and unmapped) else 0


def render_markdown(manifest) -> str:
    lines = [
        "# API coverage",
        "",
        "Generated by `tools/generate_coverage.py` from the deployed API's controllers, and enforced by",
        "`ContractCoverageTests`. Every operation below is either mapped to a typed SDK method or",
        "classified with a reason; a release cannot ship with an unmapped one.",
        "",
        f"- Operations: **{manifest['operationCount']}**",
        f"- Mapped to a typed method: **{manifest['mappedCount']}**",
        f"- Classified as not-for-clients: **{manifest['classifiedCount']}**",
        f"- Unmapped: **{len(manifest['unmapped'])}**",
        "",
        "## WebSocket protocols",
        "",
        "| Protocol | Route | Connection |",
        "| --- | --- | --- |",
    ]
    for socket in manifest["sockets"]:
        lines.append(f"| {socket['protocol']} | `{socket['path']}` | `{socket['sdk']}` |")

    lines += ["", "## REST operations", "", "| Method | Route | SDK operation |", "| --- | --- | --- |"]
    for operation in manifest["operations"]:
        lines.append(f"| {operation['method']} | `{operation['path']}` | `{operation['sdkOperation']}` |")

    lines += ["", "## Classified", "", "| Method | Route | Why |", "| --- | --- | --- |"]
    for operation in manifest["classified"]:
        lines.append(f"| {operation['method']} | `{operation['path']}` | {operation['classification']} |")

    lines.append("")
    return "\n".join(lines)


def render_csharp(manifest) -> str:
    def row(operation, extra):
        return ('            new StarhermitOperationCoverage("%s", "%s", "%s", %s),\n'
                % (operation["method"], operation["path"], operation["operationId"], extra))

    lines = [
        "// <auto-generated>\n",
        "//     Written by tools/generate_coverage.py from the deployed API's controllers.\n",
        "//     Do not edit by hand: run the generator after any API change.\n",
        "// </auto-generated>\n",
        "\n",
        "#nullable enable\n",
        "\n",
        "namespace Starhermit.Tests\n",
        "{\n",
        "    /// <summary>One API operation and how this SDK covers it.</summary>\n",
        "    public readonly struct StarhermitOperationCoverage\n",
        "    {\n",
        "        /// <summary>Creates a coverage row.</summary>\n",
        "        /// <param name=\"method\">HTTP method.</param>\n",
        "        /// <param name=\"path\">Route shape, with parameters reduced to {}.</param>\n",
        "        /// <param name=\"apiOperation\">The controller action that serves it.</param>\n",
        "        /// <param name=\"sdkOperation\">The SDK operation id, or null when classified.</param>\n",
        "        /// <param name=\"classification\">Why the SDK does not type it, when it does not.</param>\n",
        "        public StarhermitOperationCoverage(string method, string path, string apiOperation, string? sdkOperation, string? classification = null)\n",
        "        {\n",
        "            Method = method;\n",
        "            Path = path;\n",
        "            ApiOperation = apiOperation;\n",
        "            SdkOperation = sdkOperation;\n",
        "            Classification = classification;\n",
        "        }\n",
        "\n",
        "        /// <summary>HTTP method.</summary>\n        public string Method { get; }\n\n",
        "        /// <summary>Route shape.</summary>\n        public string Path { get; }\n\n",
        "        /// <summary>Controller action that serves it.</summary>\n        public string ApiOperation { get; }\n\n",
        "        /// <summary>SDK operation id, or null when classified.</summary>\n        public string? SdkOperation { get; }\n\n",
        "        /// <summary>Why the SDK does not type it.</summary>\n        public string? Classification { get; }\n",
        "    }\n",
        "\n",
        "    /// <summary>The generated coverage manifest the contract test enforces.</summary>\n",
        "    public static class ApiCoverage\n",
        "    {\n",
        "        /// <summary>Operations this SDK maps to a typed method.</summary>\n",
        "        public static readonly StarhermitOperationCoverage[] Mapped =\n",
        "        {\n",
    ]
    for operation in manifest["operations"]:
        lines.append(row(operation, '"%s"' % operation["sdkOperation"]))
    lines.append("        };\n\n")
    lines.append("        /// <summary>Operations deliberately left untyped, each with its reason.</summary>\n")
    lines.append("        public static readonly StarhermitOperationCoverage[] Classified =\n        {\n")
    for operation in manifest["classified"]:
        lines.append(row(operation, 'null, "%s"' % operation["classification"].replace('"', '\\"')))
    lines.append("        };\n\n")
    lines.append("        /// <summary>Operations with no mapping at all. A release must not ship with any.</summary>\n")
    lines.append("        public static readonly StarhermitOperationCoverage[] Unmapped =\n        {\n")
    for operation in manifest["unmapped"]:
        lines.append(row(operation, "null"))
    lines.append("        };\n\n")
    lines.append("        /// <summary>WebSocket protocols and the connection type that speaks each one.</summary>\n")
    lines.append("        public static readonly (string Protocol, string Path, string Connection)[] Sockets =\n        {\n")
    for socket in manifest["sockets"]:
        lines.append('            ("%s", "%s", "%s"),\n' % (socket["protocol"], socket["path"], socket["sdk"]))
    lines.append("        };\n")
    lines.append("    }\n}\n")
    return "".join(lines)


if __name__ == "__main__":
    sys.exit(main())
