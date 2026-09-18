const fs = require('fs');
const path = require('path');
const https = require('https');
const { execFileSync } = require('child_process');

const BASE_URL = process.env.ASSET_STORE_URL || 'https://kharma.unity3d.com';
const TOOLS_MANIFEST = 'Packages/com.unity.asset-store-tools/package.json';
const PROJECT_VERSION = 'ProjectSettings/ProjectVersion.txt';
const FALLBACK_TOOL_VERSION = '12.0.5';
const REGISTRY_KEY = 'HKCU\\Software\\Unity Technologies\\Unity Editor 5.x';

function readCachedSession() {
    if (process.platform !== 'win32') return null;

    let output;
    try {
        output = execFileSync('reg', ['query', REGISTRY_KEY], { encoding: 'utf8' });
    } catch {
        return null;
    }

    const match = /^\s*kharma\.sessionid_h\d+\s+REG_BINARY\s+([0-9A-Fa-f]+)\s*$/m.exec(output);
    if (!match) return null;

    return Buffer.from(match[1], 'hex').toString('utf8').replace(/\0+$/, '');
}

function resolveSession() {
    const fromEnv = process.env.ASSET_STORE_SESSION;
    if (fromEnv) return fromEnv.trim();

    const cached = readCachedSession();
    if (cached) return cached;

    throw new Error('No session available. Set ASSET_STORE_SESSION, or log in through Tools > Asset Store > Uploader on this machine.');
}

function resolveUnityVersion(explicit) {
    if (explicit) return explicit;

    if (fs.existsSync(PROJECT_VERSION)) {
        const match = /^m_EditorVersion:\s*(.+)$/m.exec(fs.readFileSync(PROJECT_VERSION, 'utf8'));
        if (match) return match[1].trim();
    }

    throw new Error(`Cannot determine the Unity version: ${PROJECT_VERSION} is missing. Pass --unity-version.`);
}

function resolveToolVersion(explicit) {
    if (explicit) return explicit;

    if (fs.existsSync(TOOLS_MANIFEST)) {
        return JSON.parse(fs.readFileSync(TOOLS_MANIFEST, 'utf8')).version;
    }

    return FALLBACK_TOOL_VERSION;
}

function buildUrl(pathname, query) {
    const url = new URL(pathname, BASE_URL);
    for (const [key, value] of Object.entries(query)) url.searchParams.set(key, value);
    return url;
}

function request(options) {
    const { method, url, session, body, bodyStream, bodyLength, onProgress } = options;

    return new Promise((resolve, reject) => {
        const headers = { Accept: 'application/json' };
        if (session) headers['X-Unity-Session'] = session;
        if (body !== undefined) {
            headers['Content-Type'] = 'application/x-www-form-urlencoded';
            headers['Content-Length'] = Buffer.byteLength(body);
        }
        if (bodyLength !== undefined) headers['Content-Length'] = bodyLength;

        const req = https.request(url, { method, headers, timeout: 0 }, res => {
            const chunks = [];
            res.on('data', chunk => chunks.push(chunk));
            res.on('end', () => resolve({ status: res.statusCode, body: Buffer.concat(chunks).toString('utf8') }));
        });

        req.on('error', reject);

        if (bodyStream) {
            let sent = 0;
            bodyStream.on('data', chunk => {
                sent += chunk.length;
                if (onProgress) onProgress(sent, bodyLength);
            });
            bodyStream.on('error', reject);
            bodyStream.pipe(req);
            return;
        }

        if (body !== undefined) req.write(body);
        req.end();
    });
}

function parseAssetStoreJson(response, what) {
    let json;
    try {
        json = JSON.parse(response.body);
    } catch {
        throw new Error(`${what} returned HTTP ${response.status} with a non-JSON body:\n${response.body.slice(0, 500)}`);
    }

    if (json.error) throw new Error(`${what} failed: ${json.error}`);
    if (json.status && json.status !== 'ok' && json.message) throw new Error(`${what} failed: ${json.message}`);
    if (response.status < 200 || response.status >= 300) throw new Error(`${what} returned HTTP ${response.status}`);

    return json;
}

async function login(session, unityVersion, toolVersion) {
    const query = { unityversion: unityVersion, toolversion: `V${toolVersion}` };
    const body = new URLSearchParams({ ...query, reuse_session: session }).toString();
    const response = await request({ method: 'POST', url: buildUrl('/login', query), body });
    const json = parseAssetStoreJson(response, 'Login');

    if (!json.xunitysession) throw new Error('Login succeeded but returned no session id.');
    if (!json.publisher) throw new Error(`Unity ID ${json.username} is not connected to a publisher account.`);

    return { session: json.xunitysession, publisher: json.publisher, username: json.username, name: json.name };
}

async function getPackages(session, unityVersion, toolVersion) {
    const query = { unityversion: unityVersion, toolversion: `V${toolVersion}` };
    const response = await request({ method: 'GET', url: buildUrl('/api/asset-store-tools/metadata/0.json', query), session });
    const json = parseAssetStoreJson(response, 'Package metadata');

    if (!json.packages) throw new Error('Package metadata contained no packages.');

    return Object.entries(json.packages).map(([packageId, data]) => ({
        packageId,
        versionId: data.id,
        name: data.name,
        status: data.status,
        rootGuid: data.root_guid,
        rootPath: data.root_path,
        projectPath: data.project_path,
        isCompleteProject: data.is_complete_project
    }));
}

async function getVersions(session, unityVersion, toolVersion) {
    const query = { unityversion: unityVersion, toolversion: `V${toolVersion}` };
    const response = await request({ method: 'GET', url: buildUrl('/api/management/packages.json', query), session });
    const json = parseAssetStoreJson(response, 'Package management data');

    if (!json.packages) throw new Error('Package management data contained no packages.');

    const byVersionId = new Map();
    for (const pkg of json.packages) {
        for (const version of pkg.versions || []) {
            byVersionId.set(String(version.id), {
                versionId: String(version.id),
                packageId: String(version.package_id),
                status: version.status,
                versionName: version.version_name,
                size: version.size,
                submitted: version.submitted,
                published: version.published
            });
        }
    }

    return byVersionId;
}

function assertUploadable(version, versionId, expectedVersion) {
    if (!version) {
        throw new Error(`Version ${versionId} is not present in the publisher's management data.`);
    }

    if (version.status !== 'draft') {
        throw new Error(`Version ${versionId} has status '${version.status}', not 'draft'. Create a draft in the Publisher Portal before uploading.`);
    }

    if (expectedVersion && version.versionName !== expectedVersion) {
        throw new Error(`The draft's release version is '${version.versionName}', but '${expectedVersion}' was expected. Update the Release version in the Publisher Portal, or pass the matching --expect-version.`);
    }
}

function describeRoot(packageDir) {
    const normalized = packageDir.replace(/\\/g, '/').replace(/\/+$/, '');
    const manifestPath = `${normalized}/package.json`;

    if (!fs.existsSync(manifestPath)) throw new Error(`Not a package directory: ${manifestPath} is missing`);

    const metaPath = `${manifestPath}.meta`;
    if (!fs.existsSync(metaPath)) throw new Error(`Cannot determine root_guid: ${metaPath} is missing`);

    const guidMatch = /^guid:\s*([0-9a-fA-F]{32})\s*$/m.exec(fs.readFileSync(metaPath, 'utf8'));
    if (!guidMatch) throw new Error(`Cannot determine root_guid: no guid in ${metaPath}`);

    const projectPath = JSON.parse(fs.readFileSync(manifestPath, 'utf8')).name;
    if (!projectPath) throw new Error(`Cannot determine project_path: no name in ${manifestPath}`);

    return { rootGuid: guidMatch[1], rootPath: `Packages/${projectPath}`, projectPath };
}

function assertRootMatchesStore(local, stored, allowChange) {
    const drift = ['rootGuid', 'rootPath', 'projectPath'].filter(key => stored[key] && stored[key] !== local[key]);
    if (drift.length === 0 || allowChange) return;

    const details = drift.map(key => `  ${key}: store='${stored[key]}' local='${local[key]}'`).join('\n');
    throw new Error(`The package root differs from what the Asset Store recorded for the last upload:\n${details}\nPass --allow-root-change if this is intentional.`);
}

async function upload(options) {
    const { file, packageId, packageDir, unityVersion, toolVersion, allowRootChange, expectVersion, dryRun } = options;

    if (!fs.existsSync(file)) throw new Error(`Package file not found: ${file}`);
    if (!file.endsWith('.unitypackage')) throw new Error(`Package file is not a .unitypackage: ${file}`);

    const session = resolveSession();
    const user = await login(session, unityVersion, toolVersion);
    process.stderr.write(`Authenticated as ${user.name} (publisher ${user.publisher})\n`);

    const packages = await getPackages(user.session, unityVersion, toolVersion);
    const target = packages.find(p => p.packageId === String(packageId));
    if (!target) {
        throw new Error(`Package ${packageId} not found. Available: ${packages.map(p => `${p.packageId} (${p.name})`).join(', ')}`);
    }

    const versions = await getVersions(user.session, unityVersion, toolVersion);
    const version = versions.get(String(target.versionId));
    assertUploadable(version, target.versionId, expectVersion);

    const local = describeRoot(packageDir);
    assertRootMatchesStore(local, target, allowRootChange);

    const size = fs.statSync(file).size;
    process.stderr.write(`${dryRun ? 'Would upload' : 'Uploading'} ${(size / 1048576).toFixed(2)} MB to '${target.name}' draft ${version.versionName} (versionId=${target.versionId})\n`);

    if (dryRun) return { ...target, versionName: version.versionName, dryRun: true };

    const url = buildUrl(`/api/asset-store-tools/package/${target.versionId}/unitypackage.json`, {
        root_guid: local.rootGuid,
        root_path: local.rootPath,
        project_path: local.projectPath,
        unityversion: unityVersion,
        toolversion: `V${toolVersion}`
    });

    let lastReported = -1;
    const response = await request({
        method: 'PUT',
        url,
        session: user.session,
        bodyStream: fs.createReadStream(file),
        bodyLength: size,
        onProgress: (sent, total) => {
            const percent = Math.floor(sent / total * 100 / 5) * 5;
            if (percent === lastReported) return;
            lastReported = percent;
            process.stderr.write(`  ${percent}%\n`);
        }
    });

    parseAssetStoreJson(response, 'Upload');
    return target;
}

function parseArgs(argv) {
    const args = {};

    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case '--file': args.file = argv[++i]; break;
            case '--package-id': args.packageId = argv[++i]; break;
            case '--package-dir': args.packageDir = argv[++i]; break;
            case '--unity-version': args.unityVersion = argv[++i]; break;
            case '--tool-version': args.toolVersion = argv[++i]; break;
            case '--expect-version': args.expectVersion = argv[++i]; break;
            case '--allow-root-change': args.allowRootChange = true; break;
            case '--dry-run': args.dryRun = true; break;
            case '--reveal': args.reveal = true; break;
            default: throw new Error(`Unexpected argument: ${argv[i]}`);
        }
    }

    return args;
}

async function main() {
    const [mode, ...rest] = process.argv.slice(2);

    if (!['packages', 'upload', 'session'].includes(mode)) {
        console.error('Usage:');
        console.error('  node kharma.js packages');
        console.error('  node kharma.js upload --package-id <id> --file <file.unitypackage> --package-dir <dir> [--expect-version <x.y.z>] [--dry-run] [--allow-root-change]');
        console.error('  node kharma.js session [--reveal]');
        process.exit(1);
    }

    const args = parseArgs(rest);
    const unityVersion = resolveUnityVersion(args.unityVersion);
    const toolVersion = resolveToolVersion(args.toolVersion);

    if (mode === 'session') {
        const session = resolveSession();
        if (!args.reveal) {
            console.log(`Session available (length ${session.length}). Re-run with --reveal to print it.`);
            return;
        }
        console.log(session);
        return;
    }

    if (mode === 'packages') {
        const session = resolveSession();
        const user = await login(session, unityVersion, toolVersion);
        const versions = await getVersions(user.session, unityVersion, toolVersion);

        console.log(`${user.name} (publisher ${user.publisher})`);
        for (const p of await getPackages(user.session, unityVersion, toolVersion)) {
            const version = versions.get(String(p.versionId));
            console.log(`  ${p.packageId}  ${p.name}`);
            console.log(`      uploadable: versionId=${p.versionId} release=${version ? version.versionName : '?'} status=${version ? version.status : p.status}`);
            console.log(`      root_guid=${p.rootGuid} root_path=${p.rootPath} project_path=${p.projectPath}`);
            for (const v of [...versions.values()].filter(v => v.packageId === p.packageId)) {
                console.log(`      - ${v.versionName.padEnd(10)} ${v.status.padEnd(10)} versionId=${v.versionId} published=${v.published || '-'}`);
            }
        }
        return;
    }

    if (!args.file || !args.packageId || !args.packageDir) {
        console.error('upload requires --file, --package-id and --package-dir');
        process.exit(1);
    }

    const target = await upload({ ...args, unityVersion, toolVersion });
    console.log(target.dryRun
        ? `Dry run passed for '${target.name}' draft ${target.versionName} (packageId=${target.packageId}, versionId=${target.versionId})`
        : `Uploaded to '${target.name}' (packageId=${target.packageId}, versionId=${target.versionId})`);
}

module.exports = { login, getPackages, getVersions, upload, describeRoot, resolveSession };

if (require.main === module) {
    main().catch(e => {
        console.error(e.message);
        process.exit(1);
    });
}
