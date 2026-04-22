set(VCPKG_TARGET_ARCHITECTURE arm64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Darwin)
set(VCPKG_OSX_ARCHITECTURES arm64)
set(VCPKG_BUILD_TYPE release)

# Make the dylib files relocatable, by using the MAJOR versioned dylib files as @rpath and use @loader_path
set(VCPKG_LINKER_FLAGS "-Wl,-headerpad_max_install_names")
set(VCPKG_FIXUP_MACHO_RPATH OFF)
set(VCPKG_INSTALL_NAME_DIR "@rpath")
set(VCPKG_LINKER_FLAGS "${VCPKG_LINKER_FLAGS} -Wl,-rpath,@loader_path/")