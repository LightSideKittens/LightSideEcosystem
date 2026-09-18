const fs = require('fs');
const path = require('path');

const BASE_URL = process.env.CN_ASSET_STORE_URL || 'https://assetstore.u3d.cn';
const CHUNK_SIZE = 10485760;
const DEFAULT_SRPS = ['standard', 'custom', 'hd', 'lightweight'];

function resolveCookie() {
    const raw = process.env.CN_ASSET_STORE_COOKIE;
    if (!raw) throw new Error('No session available. Set CN_ASSET_STORE_COOKIE to the Cookie header of a signed-in assetstore.u3d.cn session.');

    const cookie = raw.replace(/\s*[\r\n]+\s*/g, ' ').trim().replace(/;\s*$/, '');

    for (const required of ['LS', '_csrf']) {
        if (!new RegExp(`(?:^|;\\s*)${required}=`).test(cookie)) {
            throw new Error(`CN_ASSET_STORE_COOKIE is missing the '${required}' cookie. Copy the whole Cookie request header from a signed-in assetstore.u3d.cn session.`);
        }
    }

    const csrf = /(?:^|;\s*)_csrf=([^;]+)/.exec(cookie);

    return { cookie, csrf: csrf[1].trim() };
}

function headers(auth, extra) {
    return {
        'Accept': 'application/json, text/plain, */*',
        'X-Requested-With': 'XMLHttpRequest',
        'X-Csrf-Token': auth.csrf,
        'Cookie': auth.cookie,
        'Origin': BASE_URL,
        'Referer': `${BASE_URL}/publisher-portal/packages`,
        ...extra
    };
}

async function call(auth, method, pathname, options = {}) {
    const response = await fetch(`${BASE_URL}${pathname}`, {
        method,
        headers: headers(auth, options.json ? { 'Content-Type': 'application/json' } : undefined),
        body: options.json ? JSON.stringify(options.json) : options.body
    });

    const text = await response.text();
    let json;
    try {
        json = JSON.parse(text);
    } catch {
        throw new Error(`${method} ${pathname} returned HTTP ${response.status} with a non-JSON body:\n${text.slice(0, 400)}`);
    }

    if (!response.ok) throw new Error(`${method} ${pathname} returned HTTP ${response.status}: ${text.slice(0, 400)}`);

    return json;
}

function findDeep(value, key) {
    if (!value || typeof value !== 'object') return undefined;
    if (value[key] !== undefined) return value[key];

    for (const nested of Object.values(value)) {
        const found = findDeep(nested, key);
        if (found !== undefined) return found;
    }

    return undefined;
}

async function getVersions(auth) {
    const json = await call(auth, 'GET', '/publisher-api/package/list?count=50&start=0&status=&name=&sortBy=&sortByOrder=');
    const list = json.versionsList;
    if (!Array.isArray(list)) throw new Error('Package list did not contain versionsList.');

    return list.map(entry => ({
        packageId: String(entry.packageId),
        versionId: String(entry.version.id),
        status: entry.version.status,
        versionName: entry.version.packageVersionName,
        packageName: entry.version.packageName,
        previousVersionId: entry.version.previousVersionId ? String(entry.version.previousVersionId) : null,
        uploads: entry.version.uploads || []
    }));
}

async function ensureDraft(auth, packageId, allowCreate = true) {
    const versions = await getVersions(auth);
    const owned = versions.filter(v => v.packageId === String(packageId));
    if (owned.length === 0) throw new Error(`Package ${packageId} was not found in the publisher's package list.`);

    const existing = owned.find(v => v.status === 'DRAFT');
    if (existing) {
        process.stderr.write(`Draft versionId=${existing.versionId} already exists (${existing.versionName})\n`);
        return existing;
    }

    if (!allowCreate) throw new Error(`Package ${packageId} has no draft, and draft creation is disabled for this run.`);

    const published = owned.find(v => v.status === 'PUBLISHED');
    if (!published) {
        throw new Error(`Package ${packageId} has neither a draft nor a published version (statuses: ${owned.map(v => v.status).join(', ')}).`);
    }

    process.stderr.write(`No draft found. Creating one from published version ${published.versionId}\n`);
    await call(auth, 'POST', `/publisher-api/package/new/${packageId}/${published.versionId}`);

    const refreshed = (await getVersions(auth)).filter(v => v.packageId === String(packageId)).find(v => v.status === 'DRAFT');
    if (!refreshed) throw new Error('Draft creation reported success, but no draft is present.');

    process.stderr.write(`Created draft versionId=${refreshed.versionId}\n`);
    return refreshed;
}

function chunkSizes(totalSize) {
    const sizes = [];
    for (let offset = 0; offset < totalSize; offset += CHUNK_SIZE) {
        sizes.push(Math.min(CHUNK_SIZE, totalSize - offset));
    }
    return sizes;
}

function describeVariant(variant) {
    return variant.isTuanjieVersion ? `Tuanjie ${variant.tuanjieSkinVersion}` : `Unity ${variant.unityVersion}`;
}

async function uploadVariant(auth, options) {
    const { file, packageId, versionId, variant, srps } = options;

    const totalSize = fs.statSync(file).size;
    const sizes = chunkSizes(totalSize);
    const fileName = path.basename(file);

    const prepared = await call(auth, 'POST', '/publisher-api/package/unitypackage/prepare', {
        json: { packageId: String(packageId), versionId: String(versionId), ...variant, sizes, dependencies: [], srps }
    });

    const publishingUploadId = findDeep(prepared, 'publishingUploadId');
    if (!publishingUploadId) {
        throw new Error(`Upload preparation did not return a publishingUploadId:\n${JSON.stringify(prepared).slice(0, 400)}`);
    }

    process.stderr.write(`  ${describeVariant(variant)}: uploadId=${publishingUploadId}, ${sizes.length} chunks\n`);

    const handle = await fs.promises.open(file, 'r');
    try {
        let offset = 0;
        for (let index = 0; index < sizes.length; index++) {
            const buffer = Buffer.alloc(sizes[index]);
            await handle.read(buffer, 0, sizes[index], offset);
            offset += sizes[index];

            const form = new FormData();
            form.append('publishingUploadId', String(publishingUploadId));
            form.append('packageId', String(packageId));
            form.append('versionId', String(versionId));
            form.append('isTuanjieVersion', String(variant.isTuanjieVersion));
            form.append('tuanjieSkinVersion', variant.tuanjieSkinVersion);
            form.append('unityVersion', variant.unityVersion);
            form.append('file', new Blob([buffer]), `${fileName}-${index}.partial`);
            form.append('index', String(index));

            await call(auth, 'POST', '/publisher-api/package/unitypackage', { body: form });
            process.stderr.write(`  ${describeVariant(variant)}: chunk ${index + 1}/${sizes.length}\n`);
        }
    } finally {
        await handle.close();
    }
}

async function upload(options) {
    const { file, packageId, tuanjieVersion, unityVersion, srps, dryRun } = options;

    if (!fs.existsSync(file)) throw new Error(`Package file not found: ${file}`);
    if (!file.endsWith('.unitypackage') && !file.endsWith('.package')) {
        throw new Error(`Package file must be a .unitypackage or .package: ${file}`);
    }

    const auth = resolveCookie();
    const draft = await ensureDraft(auth, packageId, !dryRun);

    const variants = [];
    if (tuanjieVersion) variants.push({ isTuanjieVersion: true, tuanjieSkinVersion: tuanjieVersion, unityVersion: '' });
    if (unityVersion) variants.push({ isTuanjieVersion: false, tuanjieSkinVersion: '', unityVersion });

    if (variants.length === 0) throw new Error('Nothing to upload: pass --tuanjie-version and/or --unity-version.');

    const size = fs.statSync(file).size;
    process.stderr.write(`${dryRun ? 'Would upload' : 'Uploading'} ${(size / 1048576).toFixed(2)} MB to draft ${draft.versionId} as: ${variants.map(describeVariant).join(', ')}\n`);

    if (dryRun) return { draft, variants, dryRun: true };

    for (const variant of variants) {
        await uploadVariant(auth, { file, packageId, versionId: draft.versionId, variant, srps });
    }

    return { draft, variants };
}

function parseArgs(argv) {
    const args = {};

    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case '--file': args.file = argv[++i]; break;
            case '--package-id': args.packageId = argv[++i]; break;
            case '--tuanjie-version': args.tuanjieVersion = argv[++i]; break;
            case '--unity-version': args.unityVersion = argv[++i]; break;
            case '--srp': args.srps = argv[++i].split(',').map(s => s.trim()).filter(Boolean); break;
            case '--dry-run': args.dryRun = true; break;
            default: throw new Error(`Unexpected argument: ${argv[i]}`);
        }
    }

    return args;
}

async function main() {
    const [mode, ...rest] = process.argv.slice(2);

    if (!['packages', 'upload'].includes(mode)) {
        console.error('Usage:');
        console.error('  node cn.js packages');
        console.error('  node cn.js upload --package-id <id> --file <file.unitypackage> [--tuanjie-version <v>] [--unity-version <v>] [--srp a,b] [--dry-run]');
        process.exit(1);
    }

    const args = parseArgs(rest);

    if (mode === 'packages') {
        for (const v of await getVersions(resolveCookie())) {
            console.log(`  ${v.packageId}  versionId=${v.versionId}  [${v.status}]  ${v.versionName || '-'}  ${v.packageName || ''}`);
            for (const u of v.uploads) {
                console.log(`      upload: ${u.isTuanjieVersion ? `Tuanjie ${u.tuanjieSkinVersion}` : `Unity ${u.unityVersion}`}  size=${u.size || '?'}`);
            }
        }
        return;
    }

    if (!args.file || !args.packageId) {
        console.error('upload requires --file and --package-id');
        process.exit(1);
    }

    const result = await upload({ ...args, srps: args.srps || DEFAULT_SRPS });
    console.log(result.dryRun
        ? `Dry run passed for draft ${result.draft.versionId}`
        : `Uploaded ${result.variants.length} package variant(s) to draft ${result.draft.versionId}`);
}

module.exports = { getVersions, ensureDraft, upload, resolveCookie };

if (require.main === module) {
    main().catch(e => {
        console.error(e.message);
        process.exit(1);
    });
}
