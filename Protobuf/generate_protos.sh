#!/usr/bin/env bash

set -euo pipefail

proto_file="game_state.proto"
protoc_bin="protoc"

usage() {
  cat <<'EOF'
Usage: ./generate_protos.sh [options]

Options:
  --proto-file PATH   Path to the .proto file (default: game_state.proto)
  --protoc BIN        Protoc executable name/path (default: protoc)
  -h, --help          Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --proto-file)
      proto_file="$2"
      shift 2
      ;;
    --protoc)
      protoc_bin="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
csharp_out="$repo_root/Assets/Scripts/PacMan/Network/Generated"

cd "$script_dir"

if [[ ! -f "$proto_file" ]]; then
  echo "Proto file not found: $proto_file" >&2
  exit 1
fi

"$protoc_bin" -I ./ --csharp_out="$csharp_out" "$proto_file"

echo "Protobuf generation completed."
