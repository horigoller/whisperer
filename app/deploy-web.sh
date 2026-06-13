#!/usr/bin/env bash
# Build the React console and sync it to the stack's WebBucket.
# Usage: app/deploy-web.sh [stack-name] [region] [aws-profile]
set -euo pipefail

STACK="${1:-whatsapp-messaging}"
REGION="${2:-us-east-1}"
PROFILE="${3:-}"
PROFILE_ARG=()
[ -n "$PROFILE" ] && PROFILE_ARG=(--profile "$PROFILE")

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "Building React app…"
npm --prefix "$ROOT/app/web" ci
npm --prefix "$ROOT/app/web" run build

BUCKET=$(aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" "${PROFILE_ARG[@]}" \
  --query "Stacks[0].Outputs[?OutputKey=='WebBucketName'].OutputValue" --output text)

echo "Syncing to s3://$BUCKET …"
# Hashed assets get long cache; index.html must not be cached so new deploys are picked up.
aws s3 sync "$ROOT/app/web/dist" "s3://$BUCKET" --delete --exclude index.html \
  --cache-control "public,max-age=31536000,immutable" --region "$REGION" "${PROFILE_ARG[@]}"
aws s3 cp "$ROOT/app/web/dist/index.html" "s3://$BUCKET/index.html" \
  --cache-control "no-cache" --content-type "text/html" --region "$REGION" "${PROFILE_ARG[@]}"

echo "Done. App URL:"
aws cloudformation describe-stacks --stack-name "$STACK" --region "$REGION" "${PROFILE_ARG[@]}" \
  --query "Stacks[0].Outputs[?OutputKey=='AppUrl'].OutputValue" --output text
