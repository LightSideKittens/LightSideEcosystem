"""
Firebase Test Lab device parks and the devices one run uses.

A run without a selection uses its platform's standard park, or the legacy Android park on request. A selection lists
models of any park of either platform, comma-separated and case-insensitive; each platform runs the listed models of its
own parks and fails when the list names none of them.

  firebase-devices.py args <Android|iOS> --devices LIST --legacy BOOL   one "--device" / "model=…,version=…" pair per two lines
  firebase-devices.py armv7 --devices LIST --legacy BOOL                "true" when an Android device of the run needs 32-bit ARM
"""
import argparse

PARKS = {
    'Android': {
        'standard': {'shiba': '34', 'dm3q': '34', 'frankel': '36', 'pa3q': '35', 'a06': '35', 'SH-01L': '28'},
        'legacy': {'RMX3231': '30', 'aruba': '30', 'TECNO-BF6': '31'},
    },
    'iOS': {
        'standard': {'iphone14pro': '16.6', 'iphone16pro': '18.3', 'iphonese3': '26.3'},
    },
}

ARMV7_PARKS = {'legacy'}


def select(platform, selection, legacy):
    """The (park, model, version) triples a run of the platform uses."""
    names = [name.strip() for name in selection.split(',') if name.strip()]
    if not names:
        park = 'legacy' if legacy and platform == 'Android' else 'standard'
        return [(park, model, version) for model, version in PARKS[platform][park].items()]

    known = {model.lower(): (platform_name, park, model, version)
             for platform_name, parks in PARKS.items()
             for park, devices in parks.items()
             for model, version in devices.items()}
    unknown = [name for name in names if name.lower() not in known]
    if unknown:
        listed = ', '.join(entry[2] for entry in known.values())
        raise SystemExit(f"Unknown Firebase device model(s): {', '.join(unknown)}. Known models: {listed}.")
    chosen = [known[name.lower()][1:] for name in names if known[name.lower()][0] == platform]
    if not chosen:
        raise SystemExit(f"The device selection '{selection}' names no {platform} device.")
    return chosen


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest='command', required=True)
    args_command = commands.add_parser('args')
    args_command.add_argument('platform', choices=list(PARKS))
    armv7_command = commands.add_parser('armv7')
    for command in (args_command, armv7_command):
        command.add_argument('--devices', default='')
        command.add_argument('--legacy', default='false')
    options = parser.parse_args()
    legacy = options.legacy.lower() == 'true'

    if options.command == 'armv7':
        devices = select('Android', options.devices, legacy)
        print('true' if any(park in ARMV7_PARKS for park, _, _ in devices) else 'false')
        return

    for _, model, version in select(options.platform, options.devices, legacy):
        print('--device')
        print(f'model={model},version={version}')


if __name__ == '__main__':
    main()
