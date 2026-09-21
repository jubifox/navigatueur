# Build environment for the Linux port — source this before any cargo command:
#   . linux/packaging/env.sh
#
# Everything lives under $TOOLCHAIN, inside the user's own storage. No root,
# no apt, no system-wide install: this is what makes the whole chain usable on
# a locked-down machine (and it is the same constraint the shipped app honours).
# Override to put the toolchain elsewhere:  TOOLCHAIN=~/nv-toolchain . env.sh
TOOLCHAIN=${TOOLCHAIN:-/Virtual/jmasse/toolchain}
SYSROOT=$TOOLCHAIN/sysroot

export RUSTUP_HOME=$TOOLCHAIN/rustup
export CARGO_HOME=$TOOLCHAIN/cargo
export PATH=$CARGO_HOME/bin:$PATH

# webkit2gtk comes from the private sysroot; gtk/pango/cairo/glib come from the
# system. PKG_CONFIG_SYSROOT_DIR is deliberately NOT set — it would rewrite the
# system include paths into the sysroot too, where they do not exist.
export PKG_CONFIG_PATH=$SYSROOT/usr/lib/x86_64-linux-gnu/pkgconfig:/usr/lib/x86_64-linux-gnu/pkgconfig
unset PKG_CONFIG_SYSROOT_DIR

# Needed at link time, and at run time for a binary launched from the build tree.
export LD_LIBRARY_PATH=$SYSROOT/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export LIBRARY_PATH=$SYSROOT/usr/lib/x86_64-linux-gnu${LIBRARY_PATH:+:$LIBRARY_PATH}

# WebKitGTK spawns WebKitNetworkProcess / WebKitWebProcess from a path baked in
# at compile time: /usr/lib/x86_64-linux-gnu/webkit2gtk-4.1. WEBKIT_EXEC_PATH,
# which used to override it, was removed upstream — 2.52 no longer reads it
# (verified with `strings` on the shipped library). Only the injected-bundle
# path is still overridable:
export WEBKIT_INJECTED_BUNDLE_PATH=$SYSROOT/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1/injected-bundle

# Consequence: the app CANNOT be launched from inside a sandbox whose /usr is
# read-only and lacks webkit2gtk (e.g. the Flatpak SDK this was built in) —
# the helper processes are looked up at the absolute path above, which cannot
# be redirected. Building and testing (`cargo test`) work fine there; running
# the GUI needs a host that has libwebkit2gtk-4.1 installed, which is the
# normal case on a Debian/Ubuntu desktop.
