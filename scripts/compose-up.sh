#!/usr/bin/env bash
set -euo pipefail

# Nested/CI Docker hosts sometimes need bridge forwarding for service DNS.
if [[ "${EUID:-$(id -u)}" -eq 0 ]]; then
  sysctl -w net.ipv4.ip_forward=1 >/dev/null || true
  modprobe br_netfilter 2>/dev/null || true
  sysctl -w net.bridge.bridge-nf-call-iptables=1 >/dev/null 2>&1 || true
else
  sudo sysctl -w net.ipv4.ip_forward=1 >/dev/null 2>&1 || true
  sudo modprobe br_netfilter 2>/dev/null || true
  sudo sysctl -w net.bridge.bridge-nf-call-iptables=1 >/dev/null 2>&1 || true
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [[ ! -f .env ]]; then
  echo "Missing .env — copy .env.example and fill secrets." >&2
  exit 1
fi

DOCKER_BUILDKIT="${DOCKER_BUILDKIT:-0}" docker compose up --build "$@"
