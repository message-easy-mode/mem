#!/usr/bin/env node

import { createHash } from "node:crypto"
import { spawnSync } from "node:child_process"
import { promises as fs } from "node:fs"
import os from "node:os"
import path from "node:path"
import process from "node:process"
import { fileURLToPath } from "node:url"

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url))
const webRoot = path.resolve(scriptDirectory, "..")
const destinationRoot = path.join(webRoot, "docs")
const memRepositoryRoot = path.resolve(webRoot, "..", "..", "..")
const defaultWebsiteRoot = path.resolve(memRepositoryRoot, "..", "mem-website")
const websiteRoot = path.resolve(
  process.env.MEM_WEBSITE_ROOT?.trim() || getOptionValue("--source") || defaultWebsiteRoot,
)
const sourceRoot = path.join(websiteRoot, "docs")

function fail(message) {
  throw new Error(message)
}

function getOptionValue(name) {
  const index = process.argv.indexOf(name)
  if (index < 0) return undefined
  const value = process.argv[index + 1]
  if (!value || value.startsWith("--")) fail(`${name} requires a path.`)
  return value
}

function usage() {
  console.log(`Usage:
  node scripts/sync-documentation-from-website.mjs --sync [--source <mem-website-root>]
  node scripts/sync-documentation-from-website.mjs --check [--source <mem-website-root>]

Canonical source:
  ${defaultWebsiteRoot}/docs

MEM snapshot:
  ${destinationRoot}

Environment override:
  MEM_WEBSITE_ROOT=/path/to/mem-website

--sync validates the website, atomically replaces the MEM docs snapshot, rebuilds
and validates the MEM embedded release pack, then verifies exact source equality.
--check is read-only and verifies website authority, MEM source equality, and the
MEM generated release pack.
`)
}

async function assertDirectory(directory, description) {
  let stat
  try {
    stat = await fs.lstat(directory)
  } catch (error) {
    if (error?.code === "ENOENT") fail(`${description} not found: ${directory}`)
    throw error
  }
  if (stat.isSymbolicLink() || !stat.isDirectory()) {
    fail(`${description} must be a real directory, not a symlink: ${directory}`)
  }
}

async function listRegularFiles(root) {
  const files = []
  async function visit(directory) {
    const entries = await fs.readdir(directory, { withFileTypes: true })
    for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
      const absolute = path.join(directory, entry.name)
      const stat = await fs.lstat(absolute)
      if (stat.isSymbolicLink()) fail(`Documentation source may not contain symlinks: ${absolute}`)
      if (stat.isDirectory()) await visit(absolute)
      else if (stat.isFile()) files.push(path.relative(root, absolute).split(path.sep).join("/"))
      else fail(`Documentation source may contain only regular files: ${absolute}`)
    }
  }
  await visit(root)
  return files.sort((a, b) => a.localeCompare(b))
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex")
}

async function compareTrees(leftRoot, rightRoot) {
  const [left, right] = await Promise.all([listRegularFiles(leftRoot), listRegularFiles(rightRoot)])
  if (left.length !== right.length) fail(`Documentation source file count differs: website=${left.length}, MEM=${right.length}`)
  for (let i = 0; i < left.length; i += 1) {
    if (left[i] !== right[i]) fail(`Documentation source paths differ: website='${left[i]}', MEM='${right[i]}'`)
    const [a, b] = await Promise.all([fs.readFile(path.join(leftRoot, left[i])), fs.readFile(path.join(rightRoot, right[i]))])
    if (sha256(a) !== sha256(b)) fail(`MEM documentation snapshot differs from website canonical source: ${left[i]}`)
  }
  return left.length
}

function runNpm(cwd, args, description) {
  const npm = process.platform === "win32" ? "npm.cmd" : "npm"
  const result = spawnSync(npm, args, { cwd, stdio: "inherit", env: process.env })
  if (result.error) fail(`${description}: ${result.error.message}`)
  if (result.status !== 0) fail(`${description} failed with exit code ${result.status}.`)
}

function validateWebsite() {
  runNpm(websiteRoot, ["run", "docs:check"], "Canonical website docs:check")
}

function buildAndValidateMem() {
  runNpm(webRoot, ["run", "docs:build"], "MEM docs:build")
  runNpm(webRoot, ["run", "docs:check"], "MEM docs:check")
}

async function copyTree(source, destination) {
  await fs.mkdir(destination, { recursive: true })
  for (const relative of await listRegularFiles(source)) {
    const target = path.join(destination, relative)
    await fs.mkdir(path.dirname(target), { recursive: true })
    await fs.copyFile(path.join(source, relative), target)
  }
}

async function synchronize() {
  await assertDirectory(websiteRoot, "MEM website root")
  await assertDirectory(sourceRoot, "Canonical website documentation source")
  await assertDirectory(destinationRoot, "Existing MEM documentation snapshot")
  validateWebsite()

  const parent = path.dirname(destinationRoot)
  const staging = await fs.mkdtemp(path.join(parent, ".docs-website-sync-"))
  const backup = path.join(parent, `.docs-before-website-sync-${process.pid}-${Date.now()}`)
  let moved = false

  try {
    await copyTree(sourceRoot, staging)
    await compareTrees(sourceRoot, staging)
    await fs.rename(destinationRoot, backup)
    moved = true
    await fs.rename(staging, destinationRoot)

    try {
      buildAndValidateMem()
      const count = await compareTrees(sourceRoot, destinationRoot)
      await fs.rm(backup, { recursive: true, force: true })
      moved = false
      console.log(`Documentation authority sync complete: ${count} canonical source files copied website → MEM.`)
      console.log(`Source:      ${sourceRoot}`)
      console.log(`MEM snapshot: ${destinationRoot}`)
    } catch (error) {
      await fs.rm(destinationRoot, { recursive: true, force: true })
      await fs.rename(backup, destinationRoot)
      moved = false
      buildAndValidateMem()
      throw error
    }
  } finally {
    await fs.rm(staging, { recursive: true, force: true })
    if (moved) {
      const destinationExists = await fs.lstat(destinationRoot).then(() => true).catch(() => false)
      if (!destinationExists) await fs.rename(backup, destinationRoot)
    }
  }
}

async function check() {
  await assertDirectory(websiteRoot, "MEM website root")
  await assertDirectory(sourceRoot, "Canonical website documentation source")
  await assertDirectory(destinationRoot, "MEM documentation snapshot")
  validateWebsite()
  runNpm(webRoot, ["run", "docs:check"], "MEM docs:check")
  const count = await compareTrees(sourceRoot, destinationRoot)
  console.log(`Documentation authority check passed: ${count} canonical source files match website ↔ MEM.`)
}

async function main() {
  if (process.argv.includes("--help") || process.argv.includes("-h")) return usage()
  const sync = process.argv.includes("--sync")
  const checkOnly = process.argv.includes("--check")
  if (sync === checkOnly) {
    usage()
    fail("Choose exactly one mode: --sync or --check.")
  }
  if (sync) await synchronize()
  else await check()
}

main().catch((error) => {
  console.error(`ERROR: ${error instanceof Error ? error.message : error}`)
  process.exitCode = 1
})
