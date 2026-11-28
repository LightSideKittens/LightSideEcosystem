"""The phones the page reports, by the device token of their result files: label, tier, release year and platform group.
A result from a device missing here stops the reduction: a new phone gets its tier before its numbers are published.
Hosted desktop and web runners are not phones and never enter the page."""

PHONES = {
    'frankel': {'label': 'Pixel 10', 'tier': 'flagship', 'year': 2025, 'group': 'android'},
    'pa3q': {'label': 'Galaxy S25 Ultra', 'tier': 'flagship', 'year': 2025, 'group': 'android'},
    'iphone16pro': {'label': 'iPhone 16 Pro', 'tier': 'flagship', 'year': 2024, 'group': 'ios'},
    'shiba': {'label': 'Pixel 8', 'tier': 'flagship', 'year': 2023, 'group': 'android'},
    'dm3q': {'label': 'Galaxy S23 Ultra', 'tier': 'flagship', 'year': 2023, 'group': 'android'},
    'iphonese3': {'label': 'iPhone SE 3', 'tier': 'mid', 'year': 2022, 'group': 'ios'},
    'a06': {'label': 'Galaxy A06', 'tier': 'entry', 'year': 2024, 'group': 'android'},
    'SH-01L': {'label': 'AQUOS sense2 SH-01L', 'tier': 'entry', 'year': 2018, 'group': 'android'},
    'aruba': {'label': 'moto e20', 'tier': 'entry', 'year': 2021, 'group': 'android'},
}
PHONE_PLATFORMS = ('Android', 'iOS')


def device_token(file_name):
    """The device token of a per-device result file, `benchmarkResults_<n>_<device>-<api>-<locale>-<orientation>.json`."""
    stem = file_name[len('benchmarkResults_'):-len('.json')].split('_', 1)[-1]
    parts = stem.split('-')
    return '-'.join(parts[:-3]) if len(parts) > 3 else stem


def phone(token):
    """The page's entry for a phone token; raises for a phone the page does not know."""
    entry = PHONES.get(token)
    if entry is None:
        raise SystemExit(f'device {token!r} has no entry in devices.PHONES: add its label, tier, year and group first')
    return entry
