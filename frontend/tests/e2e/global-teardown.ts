/// <reference types="node" />
import { readdir, unlink } from 'fs/promises';
import { tmpdir } from 'os';
import { join } from 'path';

// Delete any temp SQLite files left by the E2E backend (ipam-e2e-<timestamp>.db
// plus the accompanying -shm / -wal WAL files if present).
export default async function globalTeardown() {
	const tmp = tmpdir();
	const entries = await readdir(tmp);
	await Promise.all(
		entries.filter((f) => /^ipam-e2e-\d+\.db/.test(f)).map((f) => unlink(join(tmp, f)).catch(() => undefined)),
	);
}
