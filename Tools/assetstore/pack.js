const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');

const PLUGIN_FOLDER_EXTENSIONS = ['androidlib', 'bundle', 'plugin', 'framework', 'xcframework'];
const BLOCK = 512;

function shouldHaveMeta(assetPath) {
    if (!assetPath) return false;
    if (assetPath.toLowerCase().endsWith('.meta')) return false;
    if (assetPath.endsWith('~')) return false;
    return !path.basename(assetPath).startsWith('.');
}

function belongsToPlugin(assetPath) {
    const lower = assetPath.toLowerCase();
    return PLUGIN_FOLDER_EXTENSIONS.some(ext => lower.includes(`.${ext}/`));
}

function readMetaGuid(metaPath) {
    if (!fs.existsSync(metaPath)) return null;
    const match = /^guid:\s*([0-9a-fA-F]{32})\s*$/m.exec(fs.readFileSync(metaPath, 'utf8'));
    return match ? match[1] : null;
}

function collectAssetPaths(root) {
    const paths = [];
    const entries = fs.readdirSync(root, { withFileTypes: true });
    const files = entries.filter(e => !e.isDirectory()).map(e => `${root}/${e.name}`);
    const dirs = entries.filter(e => e.isDirectory()).map(e => `${root}/${e.name}`);

    paths.push(...files);
    for (const dir of dirs) paths.push(...collectAssetPaths(dir));
    if (files.length > 0 || dirs.length > 0) paths.push(root);

    return paths;
}

function resolveGuid(assetPath) {
    if (!shouldHaveMeta(assetPath)) return null;
    if (assetPath.toLowerCase() === 'projectsettings/projectversion.txt') return null;

    const metaGuid = readMetaGuid(`${assetPath}.meta`);
    if (metaGuid) return metaGuid;

    if (belongsToPlugin(assetPath)) return crypto.createHash('md5').update(assetPath, 'utf8').digest('hex');

    return null;
}

function buildEntries(includePaths) {
    const byGuid = new Map();

    for (const includePath of includePaths) {
        if (!fs.existsSync(includePath)) {
            throw new Error(`Include path does not exist: ${includePath}`);
        }

        for (const assetPath of collectAssetPaths(includePath)) {
            const guid = resolveGuid(assetPath);
            if (!guid) continue;

            const existing = byGuid.get(guid);
            if (existing) {
                throw new Error(`Multiple assets share guid ${guid}:\n  ${existing.assetPath}\n  ${assetPath}`);
            }

            byGuid.set(guid, {
                guid,
                assetPath,
                isFile: fs.statSync(assetPath).isFile(),
                metaPath: fs.existsSync(`${assetPath}.meta`) ? `${assetPath}.meta` : null
            });
        }
    }

    return [...byGuid.values()].sort((a, b) => a.assetPath.localeCompare(b.assetPath));
}

function octal(value, length) {
    return value.toString(8).padStart(length - 1, '0') + '\0';
}

function tarHeader(name, size, mode, typeFlag, mtime) {
    if (Buffer.byteLength(name) > 100) {
        throw new Error(`Entry name exceeds the ustar limit: ${name}`);
    }

    const header = Buffer.alloc(BLOCK);
    header.write(name, 0, 100);
    header.write(octal(mode, 8), 100, 8);
    header.write(octal(0, 8), 108, 8);
    header.write(octal(0, 8), 116, 8);
    header.write(octal(size, 12), 124, 12);
    header.write(octal(mtime, 12), 136, 12);
    header.write('        ', 148, 8);
    header.write(typeFlag, 156, 1);
    header.write('ustar\0', 257, 6);
    header.write('00', 263, 2);

    let checksum = 0;
    for (const byte of header) checksum += byte;
    header.write(checksum.toString(8).padStart(6, '0') + '\0 ', 148, 8);

    return header;
}

class TarStream {
    constructor(sink, mtime) {
        this.sink = sink;
        this.mtime = mtime;
    }

    write(chunk) {
        if (!this.sink.write(chunk)) {
            return new Promise(resolve => this.sink.once('drain', resolve));
        }
        return null;
    }

    async addDirectory(name) {
        await this.write(tarHeader(`${name}/`, 0, 0o755, '5', this.mtime));
    }

    async addBuffer(name, buffer) {
        await this.write(tarHeader(name, buffer.length, 0o644, '0', this.mtime));
        await this.write(buffer);
        const remainder = buffer.length % BLOCK;
        if (remainder !== 0) await this.write(Buffer.alloc(BLOCK - remainder));
    }

    async addFile(name, filePath) {
        const size = fs.statSync(filePath).size;
        await this.write(tarHeader(name, size, 0o644, '0', this.mtime));

        const source = fs.createReadStream(filePath);
        for await (const chunk of source) await this.write(chunk);

        const remainder = size % BLOCK;
        if (remainder !== 0) await this.write(Buffer.alloc(BLOCK - remainder));
    }

    async finish() {
        await this.write(Buffer.alloc(BLOCK * 2));
    }
}

async function build(includePaths, outputPath, mtime) {
    const entries = buildEntries(includePaths);
    if (entries.length === 0) throw new Error('Nothing to export: no assets with meta files were found');

    fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });

    const file = fs.createWriteStream(outputPath);
    const gzip = zlib.createGzip({ level: 9 });
    const done = new Promise((resolve, reject) => {
        file.on('finish', resolve);
        file.on('error', reject);
        gzip.on('error', reject);
    });
    gzip.pipe(file);

    const tar = new TarStream(gzip, mtime);
    let assetCount = 0;

    for (const entry of entries) {
        await tar.addDirectory(entry.guid);
        await tar.addBuffer(`${entry.guid}/pathname`, Buffer.from(entry.assetPath, 'utf8'));

        if (entry.isFile) {
            await tar.addFile(`${entry.guid}/asset`, entry.assetPath);
            assetCount++;
        }

        if (entry.metaPath) await tar.addBuffer(`${entry.guid}/asset.meta`, fs.readFileSync(entry.metaPath));
    }

    await tar.finish();
    gzip.end();
    await done;

    return { entries: entries.length, assets: assetCount, size: fs.statSync(outputPath).size };
}

function readTar(buffer) {
    const files = new Map();
    let offset = 0;

    while (offset + BLOCK <= buffer.length) {
        const header = buffer.subarray(offset, offset + BLOCK);
        if (header[0] === 0) break;

        const name = header.subarray(0, 100).toString('utf8').replace(/\0.*$/, '');
        const size = parseInt(header.subarray(124, 136).toString('utf8').replace(/[\0 ].*$/, '').trim() || '0', 8);
        const typeFlag = String.fromCharCode(header[156]);
        offset += BLOCK;

        if (typeFlag === '0' || typeFlag === '\0') {
            files.set(name.replace(/^\.\//, ''), buffer.subarray(offset, offset + size));
        }

        offset += Math.ceil(size / BLOCK) * BLOCK;
    }

    return files;
}

function list(packagePath) {
    const files = readTar(zlib.gunzipSync(fs.readFileSync(packagePath)));
    const byGuid = new Map();

    for (const [name, content] of files) {
        const slash = name.indexOf('/');
        if (slash < 0) continue;

        const guid = name.slice(0, slash);
        const kind = name.slice(slash + 1);
        if (!byGuid.has(guid)) byGuid.set(guid, { guid });
        const entry = byGuid.get(guid);

        if (kind === 'pathname') entry.assetPath = content.toString('utf8').split('\n')[0].trim();
        else if (kind === 'asset') entry.assetSize = content.length;
        else if (kind === 'asset.meta') entry.meta = true;
        else if (kind === 'preview.png') entry.preview = true;
        else entry.extra = (entry.extra || []).concat(kind);
    }

    return [...byGuid.values()].sort((a, b) => (a.assetPath || a.guid).localeCompare(b.assetPath || b.guid));
}

function parseArgs(argv) {
    const args = { include: [] };

    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case '--include': args.include.push(argv[++i].replace(/\\/g, '/').replace(/\/+$/, '')); break;
            case '--out': args.out = argv[++i]; break;
            case '--mtime': args.mtime = Number(argv[++i]); break;
            default:
                if (!args.positional) args.positional = argv[i];
                else throw new Error(`Unexpected argument: ${argv[i]}`);
        }
    }

    return args;
}

async function main() {
    const [mode, ...rest] = process.argv.slice(2);

    if (mode !== 'build' && mode !== 'list') {
        console.error('Usage:');
        console.error('  node pack.js build --out <file.unitypackage> --include <dir> [--include <dir>...] [--mtime <epoch>]');
        console.error('  node pack.js list <file.unitypackage>');
        process.exit(1);
    }

    const args = parseArgs(rest);

    if (mode === 'build') {
        if (!args.out || args.include.length === 0) {
            console.error('build requires --out and at least one --include');
            process.exit(1);
        }

        const result = await build(args.include, args.out, args.mtime === undefined ? 0 : args.mtime);
        console.log(`${args.out}`);
        console.log(`  entries: ${result.entries} (${result.assets} files, ${result.entries - result.assets} folders)`);
        console.log(`  size:    ${(result.size / 1048576).toFixed(2)} MB`);
        return;
    }

    if (!args.positional) {
        console.error('list requires a .unitypackage path');
        process.exit(1);
    }

    for (const entry of list(args.positional)) {
        const flags = [entry.assetSize !== undefined ? `asset:${entry.assetSize}` : 'folder', entry.meta ? 'meta' : null, entry.preview ? 'preview' : null]
            .concat(entry.extra || [])
            .filter(Boolean)
            .join(' ');
        console.log(`${entry.guid}  ${entry.assetPath || '<no pathname>'}  [${flags}]`);
    }
}

module.exports = { build, list, buildEntries, shouldHaveMeta, resolveGuid };

if (require.main === module) {
    main().catch(e => {
        console.error(e.message);
        process.exit(1);
    });
}
