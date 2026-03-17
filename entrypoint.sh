#!/bin/sh
set -eu

cleanup() {
    echo "Received signal, shutting down gracefully..."
    wait
    exit 0
}
trap cleanup TERM INT

# Helper: initialize config from Data or defaults for given extension (yaml or conf)
init_config() {
    ext="$1"
    data_file="/app/Data/init.$ext"
    default_file="/app/defaults/init.$ext"
    if [ -f "$data_file" ] || [ -f "$default_file" ]; then
        src="$data_file"
        [ -f "$src" ] || src="$default_file"
        cp "$src" "/app/config/init.$ext"
        cp "$src" "/app/init.$ext"
        chmod 640 "/app/config/init.$ext" "/app/init.$ext"
        if [ "$ext" = "yaml" ]; then rm -f /app/init.conf; else rm -f /app/init.yaml; fi
        return 0
    fi
    return 1
}

# Config priority: init.yaml > init.conf (same as application)
if [ -f /app/config/init.yaml ]; then
    echo "Using existing configuration (init.yaml)..."
    cp /app/config/init.yaml /app/init.yaml
    chmod 640 /app/init.yaml
    rm -f /app/init.conf
elif [ -f /app/config/init.conf ]; then
    echo "Using existing configuration (init.conf)..."
    cp /app/config/init.conf /app/init.conf
    chmod 640 /app/init.conf
    rm -f /app/init.yaml
else
    echo "Initializing configuration..."
    # Prefer Data (populated with named volumes), fallback to defaults (when Data is bind-mounted empty)
    if init_config yaml; then
        :
    elif init_config conf; then
        :
    else
        echo "ERROR: No default config (init.yaml or init.conf) found" >&2
        exit 1
    fi
fi

if [ ! -r /app/init.yaml ] && [ ! -r /app/init.conf ]; then
    echo "ERROR: Cannot read configuration file (init.yaml or init.conf)" >&2
    exit 1
fi

if [ ! -x /app/JacRed ]; then
    echo "ERROR: Application binary is not executable" >&2
    exit 1
fi

# Start JacRed
echo "Starting JacRed (version: ${JACRED_VERSION:-unknown}) on $(date)"
echo "Architecture: $(uname -m)"
echo "User: $(id)"

exec "$@"
