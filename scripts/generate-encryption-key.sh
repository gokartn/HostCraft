#!/bin/bash

# HostCraft Encryption Key Generator
# This script generates a secure 256-bit encryption key for data-at-rest encryption

echo "HostCraft Data Encryption Key Generator"
echo "========================================"
echo ""

# Generate a secure 256-bit (32-byte) key
KEY=$(openssl rand -base64 32)

echo "Generated encryption key:"
echo "$KEY"
echo ""
echo "Add this to your environment variables:"
echo "ENCRYPTION_KEY=$KEY"
echo ""
echo "For Docker Compose, add to your .env file:"
echo "ENCRYPTION_KEY=$KEY"
echo ""
echo "For production deployment, store this key securely (e.g., in a secret manager)."
echo "Without this key, encrypted data cannot be decrypted!"
echo ""
echo "WARNING: If you lose this key, all encrypted data becomes inaccessible."
echo "Make sure to backup this key in a secure location."