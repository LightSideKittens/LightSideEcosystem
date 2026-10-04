"""Reading and identifying the viewer's site streams (run-<suite>-<stamp>.js)."""

import json


def parse_stream(text):
    """The run document a site stream pushes, or None when the text is not a stream."""
    start = text.find(".push(")
    end = text.rfind(");")
    if start < 0 or end <= start:
        return None
    try:
        return json.loads(text[start + len(".push("):end])
    except ValueError:
        return None


def run_identity(doc):
    """Identity of one suite run. A device-written stream and the stream CI derives from the same result
    document share it even though their file names and formatting differ."""
    info = doc.get("systemInfo") or {}
    return (
        doc.get("suite"),
        doc.get("timestamp"),
        info.get("deviceModel"),
        info.get("deviceName"),
        info.get("buildGuid"),
    )


def known_commit(doc):
    """Whether the document carries the commit it was built from."""
    return (doc.get("meta") or {}).get("commit") not in (None, "", "unknown")
