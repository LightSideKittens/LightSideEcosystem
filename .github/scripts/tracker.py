import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REGISTRY = ROOT / ".github" / "tracker.json"
TEMPLATES = ROOT / ".github" / "ISSUE_TEMPLATE"
TOKEN = re.compile(r"lst_[0-9A-Za-z]{32}")
HIDDEN = "lst_[hidden]"
LINK = re.compile(r'<([^>]+)>;\s*rel="next"')


def load():
    return json.loads(REGISTRY.read_text(encoding="utf-8"))


def scalar(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    return json.dumps(value, ensure_ascii=False)


def pair(key, value, indent):
    pad = " " * indent
    if isinstance(value, str) and "\n" in value:
        lines = value.rstrip("\n").split("\n")
        return [f"{pad}{key}: |"] + [f"{pad}  {line}" if line else "" for line in lines]
    if isinstance(value, (dict, list)):
        return [f"{pad}{key}:"] + emit(value, indent + 2)
    return [f"{pad}{key}: {scalar(value)}"]


def emit(node, indent=0):
    pad = " " * indent
    lines = []
    if isinstance(node, dict):
        for key, value in node.items():
            lines += pair(key, value, indent)
        return lines
    for item in node:
        if not isinstance(item, dict):
            lines.append(f"{pad}- {scalar(item)}")
            continue
        block = []
        for key, value in item.items():
            block += pair(key, value, indent + 2)
        block[0] = f"{pad}- {block[0][indent + 2:]}"
        lines += block
    return lines


def render(form):
    return "\n".join(emit(form)) + "\n"


def forms(registry):
    repo = registry["repository"]
    links = registry["links"]
    search = f"Search [existing issues](https://github.com/{repo}/issues?q=is%3Aissue) first — the problem may already be known.\n"
    caution = (
        "> [!CAUTION]\n"
        "> **Never paste access tokens.** Registry URLs in `manifest.json` and `.upmconfig.toml` contain your personal token (`lst_…`). "
        "Remove them from logs, files and screenshots before posting.\n"
    )
    product = {
        "type": "dropdown",
        "id": "product",
        "attributes": {"label": "Product", "options": [p["label"] for p in registry["products"]]},
        "validations": {"required": True},
    }
    bug = {
        "name": "🐞 Bug report",
        "description": "Something works incorrectly, crashes or fails to build.",
        "type": "Bug",
        "body": [
            {"type": "markdown", "attributes": {"value": f"{search}\n{caution}"}},
            product,
            {"type": "input", "id": "version", "attributes": {"label": "Product version", "description": "Shown in Tools → LightSide → Hub.", "placeholder": "3.12.1"}, "validations": {"required": True}},
            {"type": "input", "id": "unity", "attributes": {"label": "Unity version", "placeholder": "6000.6.0f1"}, "validations": {"required": True}},
            {"type": "dropdown", "id": "platforms", "attributes": {"label": "Platforms", "multiple": True, "options": registry["platforms"]["labels"]}, "validations": {"required": True}},
            {"type": "dropdown", "id": "pipeline", "attributes": {"label": "Render pipeline", "options": ["URP", "HDRP", "Built-in", "Not relevant"]}},
            {"type": "textarea", "id": "what", "attributes": {"label": "What happened?", "description": "What you expected and what happened instead."}, "validations": {"required": True}},
            {"type": "textarea", "id": "steps", "attributes": {"label": "Steps to reproduce", "placeholder": "1.\n2.\n3.\n"}, "validations": {"required": True}},
            {"type": "textarea", "id": "logs", "attributes": {"label": "Logs", "description": "Console output or the Editor log, with tokens removed.", "render": "shell"}},
            {"type": "textarea", "id": "media", "attributes": {"label": "Screenshots, video or a minimal project", "description": "Drag files here. A small project that reproduces the problem helps the most."}},
            {"type": "checkboxes", "id": "checks", "attributes": {"label": "Before submitting", "options": [
                {"label": "I searched existing issues and this is not a duplicate.", "required": True},
                {"label": "I removed access tokens and registry URLs from everything I pasted or attached.", "required": True},
            ]}},
        ],
    }
    feature = {
        "name": "✨ Feature request",
        "description": "Suggest a new capability or an improvement.",
        "type": "Feature",
        "body": [
            {"type": "markdown", "attributes": {"value": search}},
            product,
            {"type": "textarea", "id": "problem", "attributes": {"label": "What are you trying to do?", "description": "The task or problem behind the request."}, "validations": {"required": True}},
            {"type": "textarea", "id": "proposal", "attributes": {"label": "What would help?", "description": "How you imagine it working."}, "validations": {"required": True}},
            {"type": "textarea", "id": "workarounds", "attributes": {"label": "Workarounds you tried"}},
            {"type": "textarea", "id": "media", "attributes": {"label": "Examples, screenshots or references"}},
        ],
    }
    config = {
        "blank_issues_enabled": False,
        "contact_links": [
            {"name": "💬 Questions and help", "url": links["discord"], "about": "Ask questions and talk with the community on Discord."},
            {"name": "📖 Documentation", "url": links["documentation"], "about": "Guides, API reference and licences."},
            {"name": "🔒 Security vulnerability", "url": f"https://github.com/{repo}/security/advisories/new", "about": "Report vulnerabilities privately. Never open a public issue for them."},
        ],
    }
    return {"1-bug-report.yml": bug, "2-feature-request.yml": feature, "config.yml": config}


def write_forms(registry):
    TEMPLATES.mkdir(parents=True, exist_ok=True)
    for name, form in forms(registry).items():
        (TEMPLATES / name).write_text(render(form), encoding="utf-8", newline="\n")


def check_forms(registry):
    stale = [name for name, form in forms(registry).items()
             if not (TEMPLATES / name).is_file() or (TEMPLATES / name).read_text(encoding="utf-8") != render(form)]
    if stale:
        sys.exit(f"Issue forms do not match .github/tracker.json: {', '.join(stale)}. Run: python .github/scripts/tracker.py forms")


class GitHub:
    def __init__(self, repository):
        self.base = f"https://api.github.com/repos/{repository}/"
        self.token = os.environ["GITHUB_TOKEN"]

    def call(self, method, path, payload=None):
        url = path if path.startswith("https://") else self.base + path
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
        request = urllib.request.Request(url, data=data, method=method, headers={
            "Authorization": f"Bearer {self.token}",
            "Accept": "application/vnd.github+json",
            "User-Agent": "lightside-tracker",
        })
        try:
            with urllib.request.urlopen(request) as response:
                body = response.read()
                return (json.loads(body) if body else None), response.headers
        except urllib.error.HTTPError as error:
            raise RuntimeError(f"{method} {url} returned {error.code}: {error.read().decode('utf-8', 'replace')}") from error

    def pages(self, path):
        url = self.base + path
        while url:
            items, headers = self.call("GET", url)
            yield from items
            match = LINK.search(headers.get("Link") or "")
            url = match.group(1) if match else None


def quote(name):
    return urllib.parse.quote(name, safe="")


def labels(registry, prune):
    hub = GitHub(registry["repository"])
    wanted = {p["label"]: (p["color"], p["description"]) for p in registry["products"]}
    for name in registry["platforms"]["labels"]:
        wanted[name] = (registry["platforms"]["color"], "Platform where the problem occurs")
    token = registry["tokenLabel"]
    wanted[token["label"]] = (token["color"], token["description"])
    current = {label["name"]: label for label in hub.pages("labels?per_page=100")}
    for name, (color, description) in wanted.items():
        label = current.get(name)
        if label is None:
            hub.call("POST", "labels", {"name": name, "color": color, "description": description})
        elif label["color"].lower() != color or (label.get("description") or "") != description:
            hub.call("PATCH", f"labels/{quote(name)}", {"new_name": name, "color": color, "description": description})
    if prune:
        for name in current.keys() - wanted.keys():
            hub.call("DELETE", f"labels/{quote(name)}")


def sections(body):
    answers, current = {}, None
    for line in body.splitlines():
        if line.startswith("### "):
            current = line[4:].strip()
            answers[current] = []
        elif current is not None:
            answers[current].append(line)
    return {key: "\n".join(lines).strip() for key, lines in answers.items()}


def guard(hub, registry, number, text, path, kind):
    if not TOKEN.search(text):
        return
    hub.call("PATCH", path, {"body": TOKEN.sub(HIDDEN, text)})
    hub.call("POST", f"issues/{number}/labels", {"labels": [registry["tokenLabel"]["label"]]})
    hub.call("POST", f"issues/{number}/comments", {"body": (
        f"🔒 **An access token was found in this {kind} and hidden.**\n\n"
        "The original text stays in the edit history until a maintainer deletes that revision, "
        f"so the token must be treated as compromised. @{registry['maintainer']} will revoke it. "
        f"For a replacement token, contact {registry['links']['email']}."
    )})


def classify(hub, registry, issue):
    answers = sections(issue["body"] or "")
    if "Product" not in answers:
        return
    managed = {p["label"] for p in registry["products"]} | set(registry["platforms"]["labels"])
    chosen = {answers["Product"]} | {value.strip() for value in answers.get("Platforms", "").split(",")}
    wanted = chosen & managed
    present = {label["name"] for label in issue["labels"]} & managed
    if wanted - present:
        hub.call("POST", f"issues/{issue['number']}/labels", {"labels": sorted(wanted - present)})
    for name in present - wanted:
        hub.call("DELETE", f"issues/{issue['number']}/labels/{quote(name)}")


def intake(registry):
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    hub = GitHub(registry["repository"])
    issue = event["issue"]
    if os.environ["GITHUB_EVENT_NAME"] == "issue_comment":
        comment = event["comment"]
        guard(hub, registry, issue["number"], comment["body"] or "", f"issues/comments/{comment['id']}", "comment")
        return
    guard(hub, registry, issue["number"], issue["body"] or "", f"issues/{issue['number']}", "issue")
    classify(hub, registry, issue)


def main():
    registry = load()
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "forms":
        write_forms(registry)
    elif command == "check":
        check_forms(registry)
    elif command == "labels":
        labels(registry, "--prune" in sys.argv[2:])
    elif command == "intake":
        intake(registry)
    else:
        sys.exit("Usage: tracker.py forms | check | labels [--prune] | intake")


if __name__ == "__main__":
    main()
