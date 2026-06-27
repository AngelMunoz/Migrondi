#!/usr/bin/env bash
set -euo pipefail

# Script to download and install Migrondi CLI and/or UI for Linux and macOS

REPO_OWNER="AngelMunoz"
REPO_NAME="Migrondi"
DEFAULT_INSTALL_DIR_BASE="$HOME/.local/share"

# --- Helper Functions ---
log_info() {
    echo "[INFO] $1"
}

log_error() {
    echo "[ERROR] $1" >&2
}

# Check for required commands
check_command() {
    if ! command -v "$1" &> /dev/null; then
        log_error "Required command '$1' is not installed. Please install it and try again."
        exit 1
    fi
}

# Per-component metadata. The slug doubles as the asset prefix, the extraction
# subdirectory, and the proxy/shim name.
component_slug() {
    case "$1" in
        cli) echo "migrondi" ;;
        ui)  echo "migrondiui" ;;
        *)   return 1 ;;
    esac
}

component_exe() {
    case "$1" in
        cli) echo "Migrondi" ;;
        ui)  echo "MigrondiUI" ;;
        *)   return 1 ;;
    esac
}

# --- Argument Parsing ---
COMPONENT="cli"
INSTALL_VERSION=""
USE_LATEST=false
CUSTOM_DOWNLOAD_PATH=""
ADD_TO_PROFILE=true

usage() {
    echo "Usage: $0 [options]"
    echo ""
    echo "Options:"
    echo "  -c, --component <cli|ui|both>  Which app to install (default: cli)."
    echo "  -v, --version VERSION          Specify a version to install (e.g., v0.1.0)."
    echo "  -l, --latest                   Install the latest version (default if no version specified)."
    echo "  -p, --path PATH                Specify a custom download/installation path."
    echo "      --no-profile               Do not add Migrondi to the shell profile (PATH)."
    echo "  -h, --help                     Show this help message."
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--component)
            COMPONENT=$(printf '%s' "$2" | tr '[:upper:]' '[:lower:]')
            case "$COMPONENT" in
                cli|ui|both) ;;
                *) log_error "Invalid component '$2'. Must be 'cli', 'ui', or 'both'."; exit 1 ;;
            esac
            shift 2
            ;;
        -v|--version)
            INSTALL_VERSION="$2"
            shift 2
            ;;
        -l|--latest)
            USE_LATEST=true
            shift
            ;;
        -p|--path)
            CUSTOM_DOWNLOAD_PATH="$2"
            shift 2
            ;;
        --no-profile)
            ADD_TO_PROFILE=false
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            log_error "Unknown option: $1"
            usage
            exit 1
            ;;
    esac
done

# --- Pre-flight Checks ---
check_command "curl"
check_command "unzip"
# jq is preferred for GitHub API parsing
HAS_JQ=true
if ! command -v "jq" &> /dev/null; then
    log_info "jq command not found. Will attempt to parse GitHub API response with grep/sed, but this is less reliable. Installing jq is recommended."
    HAS_JQ=false
fi


# --- Platform Detection ---
os_type=""
os_arch=""

case "$(uname -s)" in
    Linux*)  os_type="linux" ;;
    Darwin*) os_type="osx" ;;
    *)
        log_error "Unsupported operating system: $(uname -s)"
        exit 1
        ;;
esac

case "$(uname -m)" in
    x86_64)  os_arch="x64" ;;
    arm64)   os_arch="arm64" ;;
    aarch64) os_arch="arm64" ;; # aarch64 is often reported for arm64
    *)
        log_error "Unsupported architecture: $(uname -m)"
        exit 1
        ;;
esac

selected_platform="${os_type}-${os_arch}"
log_info "Detected platform: $selected_platform"

# --- Determine Effective Install Directory ---
if [ -n "$CUSTOM_DOWNLOAD_PATH" ]; then
    effective_install_dir="$CUSTOM_DOWNLOAD_PATH"
else
    effective_install_dir="${DEFAULT_INSTALL_DIR_BASE}/Migrondi"
fi

# Ensure the target directory exists, create if not
if [ ! -d "$effective_install_dir" ]; then
    log_info "Target directory '$effective_install_dir' does not exist. Creating it..."
    if mkdir -p "$effective_install_dir"; then
        log_info "Successfully created directory: $effective_install_dir"
    else
        log_error "Failed to create directory '$effective_install_dir'."
        exit 1
    fi
fi

# Resolve to absolute path
effective_install_dir="$(cd "$effective_install_dir" && pwd)"
log_info "Migrondi will be installed in: $effective_install_dir"

# --- Determine Release Tag ---
release_tag=""
if [ -n "$INSTALL_VERSION" ]; then
    release_tag="$INSTALL_VERSION"
    log_info "Using specified version: $release_tag"
else
    latest_release_url="https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases/latest"
    log_info "Fetching latest release information..."
    response=$(curl -sL "$latest_release_url")
    if [ $? -ne 0 ]; then
        log_error "Failed to fetch latest release information from GitHub API."
        exit 1
    fi

    if $HAS_JQ; then
        release_tag=$(echo "$response" | jq -r .tag_name)
    else
        # Basic parsing if jq is not available
        release_tag=$(echo "$response" | grep -o '"tag_name": *"[^"]*"' | sed -E 's/"tag_name": *"([^"]*)"/\1/')
    fi

    if [ -z "$release_tag" ] || [ "$release_tag" == "null" ]; then
        log_error "Could not determine the latest release tag. Response was:"
        echo "$response"
        exit 1
    fi
    log_info "Using latest release tag: $release_tag"
fi

# --- Install Selected Component(s) ---
# Downloads, extracts, and creates the proxy/shim for a single component.
install_component() {
    local comp="$1"
    local slug exe asset download_url zip_file_path extraction_dir_path proxy_path
    slug="$(component_slug "$comp")"
    exe="$(component_exe "$comp")"
    asset="${slug}-${selected_platform}.zip"
    download_url="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/download/${release_tag}/${asset}"
    zip_file_path="${effective_install_dir}/${asset}"
    extraction_dir_path="${effective_install_dir}/.${slug}"
    proxy_path="${effective_install_dir}/${slug}"

    log_info "Downloading $asset to $zip_file_path from $download_url..."

    if curl -sSL -f -o "$zip_file_path" "$download_url"; then
        log_info "Successfully downloaded to $zip_file_path"
    else
        log_error "Failed to download the asset from $download_url"
        log_info "Attempting to list available assets for release $release_tag..."
        local release_assets_url assets_response
        release_assets_url="https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases/tags/${release_tag}"
        if assets_response=$(curl -sL "$release_assets_url"); then
            log_info "Available assets for release $release_tag:"
            if $HAS_JQ; then
                echo "$assets_response" | jq -r '.assets[].name' | sed 's/^/- /'
            else
                echo "$assets_response" | grep -o '"name": *"[^"]*"' | sed -E 's/"name": *"([^"]*)"/\1/' | sed 's/^/- /'
            fi
        else
            log_info "Could not retrieve asset list for tag $release_tag."
        fi
        return 1
    fi

    if [ ! -d "$extraction_dir_path" ]; then
        mkdir -p "$extraction_dir_path"
    fi

    log_info "Extracting $zip_file_path to $extraction_dir_path..."
    if unzip -qo "$zip_file_path" -d "$extraction_dir_path"; then
        log_info "Successfully extracted to $extraction_dir_path"
    else
        log_error "Failed to extract $zip_file_path."
        rm -f "$zip_file_path"
        return 1
    fi

    rm -f "$zip_file_path"
    log_info "Removed $zip_file_path"

    # Make the binary executable (zip extraction doesn't always preserve the bit)
    exe_path="${extraction_dir_path}/${exe}"
    chmod +x "$exe_path" 2>/dev/null || true

    # Lowercase command symlink (e.g. `migrondi`) -> uppercase binary (e.g. `.migrondi/Migrondi`).
    # The dot-prefixed extraction dir avoids colliding with the symlink name.
    if [ -d "$proxy_path" ]; then
        log_error "'$proxy_path' exists as a directory (leftover from an older install). Please remove it and re-run."
        return 1
    fi
    rm -f "$proxy_path"
    ln -s ".${slug}/${exe}" "$proxy_path"
    log_info "Installed '$(basename "$proxy_path")' command: $proxy_path -> .${slug}/${exe}"
}

case "$COMPONENT" in
    cli)  components=("cli") ;;
    ui)   components=("ui") ;;
    both) components=("cli" "ui") ;;
esac

for comp in "${components[@]}"; do
    if ! install_component "$comp"; then
        exit 1
    fi
done

# --- Add to Profile ---
if [ "$ADD_TO_PROFILE" = true ]; then
    path_to_add="$effective_install_dir" # This is the directory containing the proxy script(s)

    current_shell_basename=$(basename "$SHELL")
    profile_file=""

    if [ "$current_shell_basename" = "bash" ]; then
        profile_file="$HOME/.bashrc"
    elif [ "$current_shell_basename" = "zsh" ]; then
        profile_file="$HOME/.zshrc"
    else
        log_info "Unsupported shell: $current_shell_basename. Cannot automatically update PATH."
        log_info "Please add '$path_to_add' to your PATH manually."
        profile_file="" # Skip profile update
    fi

    if [ -n "$profile_file" ]; then
        log_info "Attempting to add '$path_to_add' to PATH in shell profile ($profile_file)..."

        # Ensure the profile file exists, create if not
        if [ ! -f "$profile_file" ]; then
            log_info "Profile file ($profile_file) does not exist. Creating it..."
            if touch "$profile_file"; then
                log_info "Successfully created profile file: $profile_file"
            else
                log_error "Failed to create profile file ($profile_file). Please create it manually and add '$path_to_add' to your PATH."
                profile_file="" # Skip further profile operations
            fi
        fi

        if [ -f "$profile_file" ]; then
            # Check if the directory is already in a line that modifies PATH
            # This grep is a basic check; more sophisticated checks might be needed for complex PATH setups
            if grep -q "export PATH=.*${path_to_add}" "$profile_file" && grep -q "export MIGRONDI_HOME=.*${path_to_add}" "$profile_file"; then
                log_info "'$path_to_add' appears to be already configured in the PATH and MIGRONDI_HOME is set in $profile_file."
            else
                comment="# Added by migrondi_install.sh to include Migrondi CLI"
                migrondi_home_command="export MIGRONDI_HOME=\"${path_to_add}\""
                path_add_command="export PATH=\"${path_to_add}:\$PATH\""

                # Add a newline before the comment if the file is not empty and doesn't end with a newline
                if [ -s "$profile_file" ] && [ "$(tail -c1 "$profile_file"; echo x)" != $'\nx' ]; then
                    echo "" >> "$profile_file"
                fi

                echo "" >> "$profile_file" # Ensure separation
                echo "$comment" >> "$profile_file"
                echo "$migrondi_home_command" >> "$profile_file"
                echo "$path_add_command" >> "$profile_file"
                log_info "Successfully added '$path_to_add' to PATH and set MIGRONDI_HOME in $profile_file."
                log_info "Please restart your shell session or run 'source $profile_file' to apply the changes."
            fi
        fi
    fi
else
    log_info "Skipping profile update as per --no-profile flag."
    log_info "You can manually add '$effective_install_dir' to your PATH if needed."
fi

log_info "Migrondi ($COMPONENT) installation completed successfully!"
case "$COMPONENT" in
    cli)  log_info "You can now use the 'migrondi' command." ;;
    ui)   log_info "You can now run the 'migrondiui' app." ;;
    both) log_info "You can now use the 'migrondi' command and run the 'migrondiui' app." ;;
esac

exit 0
