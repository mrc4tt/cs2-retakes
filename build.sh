#!/usr/bin/env bash
# Local build mirroring .github/workflows/plugin-build.yml (no GitHub Actions).
# Produces ./build-output (with map configs) and ./build-output-no-map-configs.
set -euo pipefail

cd "$(dirname "$0")"

CONFIG="${CONFIG:-Release}"
PLUGIN_BIN="./RetakesPlugin/bin/${CONFIG}/net8.0"
SHARED_BIN="./RetakesPluginShared/bin/${CONFIG}/net8.0"

echo "==> Cleaning previous output"
rm -rf ./build-output ./build-output-no-map-configs

echo "==> Building RetakesPluginShared (${CONFIG})"
dotnet build RetakesPluginShared/RetakesPluginShared.csproj -c "${CONFIG}"

echo "==> Building RetakesPlugin (${CONFIG})"
dotnet build RetakesPlugin/RetakesPlugin.csproj -c "${CONFIG}"

# Lays out one addons/ tree. $1 is the destination root; map_config is copied
# only when $2 is "with-map-config".
build_output_tree() {
    local dest="$1"
    local map_config="$2"
    local plugin_dir="${dest}/addons/counterstrikesharp/plugins/RetakesPlugin"
    local shared_dir="${dest}/addons/counterstrikesharp/shared/RetakesPluginShared"

    mkdir -p "${plugin_dir}" "${shared_dir}"

    cp "${PLUGIN_BIN}/RetakesPlugin.dll" "${plugin_dir}/"
    cp "${PLUGIN_BIN}/RetakesPlugin.pdb" "${plugin_dir}/"
    cp -r ./RetakesPlugin/lang "${plugin_dir}/"
    if [ "${map_config}" = "with-map-config" ]; then
        cp -r ./RetakesPlugin/map_config "${plugin_dir}/"
    fi

    cp "${SHARED_BIN}/RetakesPluginShared.dll" "${shared_dir}/"
    cp "${SHARED_BIN}/RetakesPluginShared.pdb" "${shared_dir}/"
    cp "${SHARED_BIN}/RetakesPluginShared.deps.json" "${shared_dir}/"
}

echo "==> Laying out ./build-output"
build_output_tree ./build-output with-map-config

echo "==> Laying out ./build-output-no-map-configs"
build_output_tree ./build-output-no-map-configs no-map-config

echo "==> Done."
echo "    with map configs : ./build-output"
echo "    no map configs   : ./build-output-no-map-configs"
