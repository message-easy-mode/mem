---
title: Import the target request, capture, and create the package
description: Import the public target request, confirm encryption readiness, capture the selected stack, and download the encrypted package.
section: Migrate from MEM 0.1.0
order: 40
---

# Import the target request, capture, and create the package

The target Control Plane creates the public encryption request. The Source Assistant uses it to produce a package that only the target can decrypt.

## Outcome

A fresh capture of the selected source stack is packaged as an encrypted `.memmigration.zip.age` file, with a package report and checksum evidence ready for transfer to MEM 0.2.0.

## Create the target Migration Request

On the MEM 0.2.0 target, open **Migrations** and create a secure migration intake. Complete operator step-up when requested.

Download the Migration Request JSON. The request contains the intake identity and public `age` recipient information. The target private decryption identity remains inside the target Control Plane and is never sent to the Source Assistant.

Transfer the request JSON to your operator workstation or source host through your normal secure administration path.

## Import the request

In the Source Assistant workspace:

1. Choose **Import request**.
2. Upload the JSON file or paste its complete JSON content.
3. Review the intake ID, recipient fingerprint, request kind, and expiry or validity information shown.
4. Reject an unexpected target, fingerprint mismatch, malformed request, or expired request.

## Confirm encryption readiness

Before capture, the Source Assistant requires acknowledgement that affected users have been told to protect their encrypted history. Confirm only after users have been advised not to sign out and to verify another device, Secure Backup and its recovery secret, or exported room keys.

## Create the capture

Choose a fresh capture unless the Source Assistant explicitly offers an eligible retained capture whose source identity and purpose still match this request.

Capture is a durable server-side operation. Closing or refreshing the browser does not cancel it. Wait for the Source Assistant to report completion and inspect any warnings before packaging.

The capture is read-only with respect to the live legacy stack. It creates migration working material beneath the Source Assistant state root.

## Create and download the package

Continue through:

1. **Create package**;
2. wait for encryption and package verification;
3. **Download package**;
4. **Download package report**.

Store the package and report together. Record or verify the reported SHA-256 checksum after any copy or transfer.

> [!IMPORTANT]
> The encrypted package is not a normal MEM backup. Do not rename it to look like a Backup Catalog export and do not unpack or modify it manually.

## Local deletion boundary

Deleting the completed local package removes the encrypted package and its package reports. It does not delete the source assessment, capture journal, plaintext source capture, live Matrix data, or old server.

Next: [Upload and review the old server](upload-and-review.md).
