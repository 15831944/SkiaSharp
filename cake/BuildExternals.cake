
////////////////////////////////////////////////////////////////////////////////////////////////////
// TOOLS & FUNCTIONS - the bits to make it all work
////////////////////////////////////////////////////////////////////////////////////////////////////

var InjectCompatibilityExternals = new Action<bool> ((inject) => {
    // some methods don't yet exist, so we must add the compat layer to them.
    // we need this as we can't modify the third party files
    // all we do is insert our header before all the others
    var compatHeader = "native-builds/src/WinRTCompat.h";
    var compatSource = "native-builds/src/WinRTCompat.c";
    var files = new Dictionary<FilePath, string> { 
        { "externals/skia/third_party/externals/dng_sdk/source/dng_string.cpp", "#if qWinOS" },
        { "externals/skia/third_party/externals/dng_sdk/source/dng_utils.cpp", "#if qWinOS" },
        { "externals/skia/third_party/externals/dng_sdk/source/dng_pthread.cpp", "#if qWinOS" },
        { "externals/skia/third_party/externals/zlib/deflate.c", "#include <assert.h>" },
        { "externals/skia/third_party/externals/libjpeg-turbo/simd/jsimd_x86_64.c", "#define JPEG_INTERNALS" },
        { "externals/skia/third_party/externals/libjpeg-turbo/simd/jsimd_i386.c", "#define JPEG_INTERNALS" },
        { "externals/skia/third_party/externals/libjpeg-turbo/simd/jsimd_arm.c", "#define JPEG_INTERNALS" },
        { "externals/skia/third_party/externals/libjpeg-turbo/simd/jsimd_arm64.c", "#define JPEG_INTERNALS" },
    };
    foreach (var filePair in files) {
        var file = filePair.Key;

        if (!FileExists (file))
            continue;

        var root = string.Join ("/", file.GetDirectory ().Segments.Select (x => ".."));
        var include = "#include \"" + root + "/" + compatHeader + "\"";
        
        var contents = FileReadLines (file).ToList ();
        var index = contents.IndexOf (include);
        if (index == -1 && inject) {
            Information ("Injecting modifications into third party code: {0}...", file);
            if (string.IsNullOrEmpty (filePair.Value)) {
                contents.Insert (0, include);
            } else {
                contents.Insert (contents.IndexOf (filePair.Value), include);
            }
            FileWriteLines (file, contents.ToArray ());
        } else if (index != -1 && !inject) {
            Information ("Removing injected modifications from third party code: {0}...", file);
            int idx = 0;
            if (string.IsNullOrEmpty (filePair.Value)) {
                idx = 0;
            } else {
                idx = contents.IndexOf (filePair.Value) - 1;
            }
            if (contents [idx] == include) {
                contents.RemoveAt (idx);
            }
            FileWriteLines (file, contents.ToArray ());
        }
    }
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// EXTERNALS - the native C and C++ libraries
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("externals-init")
    .IsDependentOn ("externals-angle-uwp")
    .IsDependentOn ("externals-harfbuzz")
    .Does (() =>  
{
    RunProcess (PythonToolPath, new ProcessSettings {
        Arguments = SKIA_PATH.CombineWithFilePath ("tools/git-sync-deps").FullPath,
        WorkingDirectory = SKIA_PATH.FullPath,
    });

    // insert compatibility modifications for external code
    InjectCompatibilityExternals (true);
});

// this builds the native C and C++ externals 
Task ("externals-native")
    .IsDependentOn ("externals-uwp")
    .IsDependentOn ("externals-windows")
    .IsDependentOn ("externals-osx")
    .IsDependentOn ("externals-ios")
    .IsDependentOn ("externals-tvos")
    .IsDependentOn ("externals-watchos")
    .IsDependentOn ("externals-android")
    .IsDependentOn ("externals-linux")
    .Does (() => 
{
    // copy all the native files into the output
    CopyDirectory ("./native-builds/lib/", "./output/native/");
    // copy ANGLE externals
    EnsureDirectoryExists ("./output/native/uwp/arm/");
    EnsureDirectoryExists ("./output/native/uwp/x86/");
    EnsureDirectoryExists ("./output/native/uwp/x64/");
    CopyFileToDirectory (ANGLE_PATH.CombineWithFilePath ("uwp/bin/UAP/ARM/libEGL.dll"), "./output/native/uwp/arm/");
    CopyFileToDirectory (ANGLE_PATH.CombineWithFilePath ("uwp/bin/UAP/ARM/libGLESv2.dll"), "./output/native/uwp/arm/");
    CopyFileToDirectory (ANGLE_PATH.CombineWithFilePath ("uwp/bin/UAP/Win32/libEGL.dll"), "./output/native/uwp/x86/");
    CopyFileToDirectory (ANGLE_PATH.CombineWithFilePath ("uwp/bin/UAP/Win32/libGLESv2.dll"), "./output/native/uwp/x86/");
    CopyFileToDirectory (ANGLE_PATH.CombineWithFilePath ("uwp/bin/UAP/x64/libEGL.dll"), "./output/native/uwp/x64/");
    CopyFileToDirectory (ANGLE_PATH.CombineWithFilePath ("uwp/bin/UAP/x64/libGLESv2.dll"), "./output/native/uwp/x64/");
});

// this builds the native C and C++ externals for Windows
Task ("externals-windows")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnWindows ())
    .Does (() =>  
{
    // libSkiaSharp

    var buildArch = new Action<string, string, string> ((arch, skiaArch, dir) => {
        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn.exe"), new ProcessSettings {
            Arguments = 
                "gen out/win/" + arch + " " + 
                "--args=\"" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\\\"win\\\" target_cpu=\\\"" + skiaArch + "\\\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true skia_use_dng_sdk=true" +
                "  skia_use_system_expat=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ \\\"-DSKIA_C_DLL\\\", \\\"/MD\\\", \\\"/EHsc\\\", \\\"/Zi\\\" ]" +
                "  extra_ldflags=[ \\\"/DEBUG\\\" ]" +
                "\"",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja.exe"), new ProcessSettings {
            Arguments = "-C out/win/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        RunMSBuildWithPlatformTarget ("native-builds/libSkiaSharp_windows/libSkiaSharp.sln", arch);

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/windows/" + dir);
        CopyFileToDirectory ("native-builds/libSkiaSharp_windows/bin/" + arch + "/Release/libSkiaSharp.dll", "native-builds/lib/windows/" + dir);
        CopyFileToDirectory ("native-builds/libSkiaSharp_windows/bin/" + arch + "/Release/libSkiaSharp.pdb", "native-builds/lib/windows/" + dir);
    });

    buildArch ("Win32", "x86", "x86");
    buildArch ("x64", "x64", "x64");

    // libHarfBuzzSharp

    var buildHarfBuzzArch = new Action<string, string> ((arch, dir) => {
        // build libHarfBuzzSharp
        RunMSBuildWithPlatformTarget ("native-builds/libHarfBuzzSharp_windows/libHarfBuzzSharp.sln", arch);

        // copy libHarfBuzzSharp to output
        EnsureDirectoryExists ("native-builds/lib/windows/" + dir);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_windows/bin/" + arch + "/Release/libHarfBuzzSharp.dll", "native-builds/lib/windows/" + dir);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_windows/bin/" + arch + "/Release/libHarfBuzzSharp.pdb", "native-builds/lib/windows/" + dir);
    });

    buildHarfBuzzArch ("Win32", "x86");
    buildHarfBuzzArch ("x64", "x64");
});

// this builds the native C and C++ externals for Windows UWP
Task ("externals-uwp")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnWindows ())
    .Does (() =>  
{
    // libSkiaSharp

    var buildArch = new Action<string, string, string> ((arch, skiaArch, dir) => {
        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn.exe"), new ProcessSettings {
            Arguments = 
                "gen out/winrt/" + arch + " " + 
                "--args=\"" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\\\"winrt\\\" target_cpu=\\\"" + skiaArch + "\\\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ " + 
                "    \\\"-DSKIA_C_DLL\\\", \\\"/MD\\\", \\\"/EHsc\\\", \\\"/Zi\\\", " + 
                "    \\\"-DWINAPI_FAMILY=WINAPI_FAMILY_APP\\\", \\\"-DSK_BUILD_FOR_WINRT\\\", \\\"-DSK_HAS_DWRITE_1_H\\\", \\\"-DSK_HAS_DWRITE_2_H\\\", \\\"-DNO_GETENV\\\" ]" +
                "  extra_ldflags=[ \\\"/APPCONTAINER\\\", \\\"/DEBUG\\\" ]" +
                "\"",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja.exe"), new ProcessSettings {
            Arguments = "-C out/winrt/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        RunMSBuildWithPlatformTarget ("native-builds/libSkiaSharp_uwp/libSkiaSharp.sln", arch);

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/uwp/" + dir);
        CopyFileToDirectory ("native-builds/libSkiaSharp_uwp/bin/" + arch + "/Release/libSkiaSharp.dll", "native-builds/lib/uwp/" + dir);
        CopyFileToDirectory ("native-builds/libSkiaSharp_uwp/bin/" + arch + "/Release/libSkiaSharp.pdb", "native-builds/lib/uwp/" + dir);
    });

    buildArch ("x64", "x64", "x64");
    buildArch ("Win32", "x86", "x86");
    buildArch ("ARM", "arm", "ARM");

    // libHarfBuzzSharp

    var buildHarfBuzzArch = new Action<string, string> ((arch, dir) => {
        // build libHarfBuzzSharp
        RunMSBuildWithPlatformTarget ("native-builds/libHarfBuzzSharp_uwp/libHarfBuzzSharp.sln", arch);

        // copy libHarfBuzzSharp to output
        EnsureDirectoryExists ("native-builds/lib/uwp/" + dir);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_uwp/bin/" + arch + "/Release/libHarfBuzzSharp.dll", "native-builds/lib/uwp/" + dir);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_uwp/bin/" + arch + "/Release/libHarfBuzzSharp.pdb", "native-builds/lib/uwp/" + dir);
    });

    buildHarfBuzzArch ("Win32", "x86");
    buildHarfBuzzArch ("x64", "x64");
    buildHarfBuzzArch ("ARM", "arm");
});

// this builds the native C and C++ externals for Mac OS X
Task ("externals-osx")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnMac ())
    .Does (() =>  
{
    // SkiaSharp

    var buildArch = new Action<string, string> ((arch, skiaArch) => {
        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn"), new ProcessSettings {
            Arguments = 
                "gen out/mac/" + arch + " " + 
                "--args='" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\"mac\" target_cpu=\"" + skiaArch + "\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ \"-DSKIA_C_DLL\", \"-mmacosx-version-min=10.9\" ]" +
                "  extra_ldflags=[ \"-Wl,macosx_version_min=10.9\" ]" +
                "'",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja"), new ProcessSettings {
            Arguments = "-C out/mac/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libSkiaSharp_osx/libSkiaSharp.xcodeproj",
            Target = "libSkiaSharp",
            Sdk = "macosx",
            Arch = arch,
            Configuration = "Release",
        });

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/osx/" + arch);
        CopyDirectory ("native-builds/libSkiaSharp_osx/build/Release/", "native-builds/lib/osx/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libSkiaSharp.dylib",
            WorkingDirectory = "native-builds/lib/osx/" + arch,
        });

        // re-sign with empty
        RunProcess ("codesign", new ProcessSettings {
            Arguments = "--force --sign - --timestamp=none libSkiaSharp.dylib",
            WorkingDirectory = "native-builds/lib/osx/" + arch,
        });
    });

    buildArch ("i386", "x86");
    buildArch ("x86_64", "x64");

    // create the fat dylib
    RunLipo ("native-builds/lib/osx/", "libSkiaSharp.dylib", new [] {
        (FilePath) "i386/libSkiaSharp.dylib", 
        (FilePath) "x86_64/libSkiaSharp.dylib"
    });

    // HarfBuzzSharp

    var buildHarfBuzzArch = new Action<string, string> ((arch, skiaArch) => {
        // build libHarfBuzzSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libHarfBuzzSharp_osx/libHarfBuzzSharp.xcodeproj",
            Target = "libHarfBuzzSharp",
            Sdk = "macosx",
            Arch = arch,
            Configuration = "Release",
        });

        // copy libHarfBuzzSharp to output
        EnsureDirectoryExists ("native-builds/lib/osx/" + arch);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_osx/build/Release/libHarfBuzzSharp.dylib", "native-builds/lib/osx/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libHarfBuzzSharp.dylib",
            WorkingDirectory = "native-builds/lib/osx/" + arch,
        });

        // re-sign with empty
        RunProcess ("codesign", new ProcessSettings {
            Arguments = "--force --sign - --timestamp=none libHarfBuzzSharp.dylib",
            WorkingDirectory = "native-builds/lib/osx/" + arch,
        });
    });

    buildHarfBuzzArch ("i386", "x86");
    buildHarfBuzzArch ("x86_64", "x64");

    // create the fat dylib
    RunLipo ("native-builds/lib/osx/", "libHarfBuzzSharp.dylib", new [] {
        (FilePath) "i386/libHarfBuzzSharp.dylib", 
        (FilePath) "x86_64/libHarfBuzzSharp.dylib"
    });
});

// this builds the native C and C++ externals for iOS
Task ("externals-ios")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnMac ())
    .Does (() => 
{
    // SkiaSharp

    var buildArch = new Action<string, string, string> ((sdk, arch, skiaArch) => {
        // generate native skia build files

        var specifics = "";
        // several instances of "error: type 'XXX' requires 8 bytes of alignment and the default allocator only guarantees 4 bytes [-Werror,-Wover-aligned]
        // https://groups.google.com/forum/#!topic/skia-discuss/hU1IPFwU6bI
        if (arch == "armv7" || arch == "armv7s") {
            specifics += ", \"-Wno-over-aligned\"";
        }

        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn"), new ProcessSettings {
            Arguments = 
                "gen out/ios/" + arch + " " + 
                "--args='" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\"ios\" target_cpu=\"" + skiaArch + "\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ \"-DSKIA_C_DLL\", \"-mios-version-min=8.0\" " + specifics + " ]" +
                "  extra_ldflags=[ \"-Wl,ios_version_min=8.0\" ]" +
                "'",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja"), new ProcessSettings {
            Arguments = "-C out/ios/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libSkiaSharp_ios/libSkiaSharp.xcodeproj",
            Target = "libSkiaSharp",
            Sdk = sdk,
            Arch = arch,
            Configuration = "Release",
        });

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/ios/" + arch);
        CopyDirectory ("native-builds/libSkiaSharp_ios/build/Release-" + sdk, "native-builds/lib/ios/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libSkiaSharp",
            WorkingDirectory = "native-builds/lib/ios/" + arch + "/libSkiaSharp.framework",
        });

        // re-sign with empty
        RunProcess ("codesign", new ProcessSettings {
            Arguments = "--force --sign - --timestamp=none libSkiaSharp.framework",
            WorkingDirectory = "native-builds/lib/ios/" + arch,
        });
    });

    buildArch ("iphonesimulator", "i386", "x86");
    buildArch ("iphonesimulator", "x86_64", "x64");
    buildArch ("iphoneos", "armv7", "arm");
    buildArch ("iphoneos", "arm64", "arm64");
    
    // create the fat framework
    CopyDirectory ("native-builds/lib/ios/armv7/libSkiaSharp.framework/", "native-builds/lib/ios/libSkiaSharp.framework/");
    DeleteFile ("native-builds/lib/ios/libSkiaSharp.framework/libSkiaSharp");
    RunLipo ("native-builds/lib/ios/", "libSkiaSharp.framework/libSkiaSharp", new [] {
        (FilePath) "i386/libSkiaSharp.framework/libSkiaSharp", 
        (FilePath) "x86_64/libSkiaSharp.framework/libSkiaSharp", 
        (FilePath) "armv7/libSkiaSharp.framework/libSkiaSharp", 
        (FilePath) "arm64/libSkiaSharp.framework/libSkiaSharp"
    });

    // HarfBuzzSharp

    var buildHarfBuzzArch = new Action<string, string> ((sdk, arch) => {
        // build libHarfBuzzSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libHarfBuzzSharp_ios/libHarfBuzzSharp.xcodeproj",
            Target = "libHarfBuzzSharp",
            Sdk = sdk,
            Arch = arch,
            Configuration = "Release",
        });

        // copy libHarfBuzzSharp_ios to output
        EnsureDirectoryExists ("native-builds/lib/ios/" + arch);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_ios/build/Release-" + sdk + "/libHarfBuzzSharp.a", "native-builds/lib/ios/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libHarfBuzzSharp.a",
            WorkingDirectory = "native-builds/lib/ios/" + arch,
        });
    });

    buildHarfBuzzArch ("iphonesimulator", "i386");
    buildHarfBuzzArch ("iphonesimulator", "x86_64");
    buildHarfBuzzArch ("iphoneos", "armv7");
    buildHarfBuzzArch ("iphoneos", "arm64");
    
    // create the fat archive
    RunLipo ("native-builds/lib/ios/", "libHarfBuzzSharp.a", new [] {
        (FilePath) "i386/libHarfBuzzSharp.a", 
        (FilePath) "x86_64/libHarfBuzzSharp.a", 
        (FilePath) "armv7/libHarfBuzzSharp.a", 
        (FilePath) "arm64/libHarfBuzzSharp.a"
    });
});

// this builds the native C and C++ externals for tvOS
Task ("externals-tvos")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnMac ())
    .Does (() => 
{
    // SkiaSharp

    var buildArch = new Action<string, string, string> ((sdk, arch, skiaArch) => {
        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn"), new ProcessSettings {
            Arguments = 
                "gen out/tvos/" + arch + " " + 
                "--args='" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\"tvos\" target_cpu=\"" + skiaArch + "\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ \"-DSK_BUILD_FOR_TVOS\", \"-DSKIA_C_DLL\", \"-mtvos-version-min=9.0\" ]" +
                "  extra_ldflags=[ \"-Wl,tvos_version_min=9.0\" ]" +
                "'",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja"), new ProcessSettings {
            Arguments = "-C out/tvos/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libSkiaSharp_tvos/libSkiaSharp.xcodeproj",
            Target = "libSkiaSharp",
            Sdk = sdk,
            Arch = arch,
            Configuration = "Release",
        });

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/tvos/" + arch);
        CopyDirectory ("native-builds/libSkiaSharp_tvos/build/Release-" + sdk, "native-builds/lib/tvos/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libSkiaSharp",
            WorkingDirectory = "native-builds/lib/tvos/" + arch + "/libSkiaSharp.framework",
        });

        // re-sign with empty
        RunProcess ("codesign", new ProcessSettings {
            Arguments = "--force --sign - --timestamp=none libSkiaSharp.framework",
            WorkingDirectory = "native-builds/lib/tvos/" + arch,
        });
    });

    buildArch ("appletvsimulator", "x86_64", "x64");
    buildArch ("appletvos", "arm64", "arm64");
    
    // create the fat framework
    CopyDirectory ("native-builds/lib/tvos/arm64/libSkiaSharp.framework/", "native-builds/lib/tvos/libSkiaSharp.framework/");
    DeleteFile ("native-builds/lib/tvos/libSkiaSharp.framework/libSkiaSharp");
    RunLipo ("native-builds/lib/tvos/", "libSkiaSharp.framework/libSkiaSharp", new [] {
        (FilePath) "x86_64/libSkiaSharp.framework/libSkiaSharp", 
        (FilePath) "arm64/libSkiaSharp.framework/libSkiaSharp"
    });

    // HarfBuzzSharp

    var buildHarfBuzzArch = new Action<string, string> ((sdk, arch) => {
        // build libHarfBuzzSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libHarfBuzzSharp_tvos/libHarfBuzzSharp.xcodeproj",
            Target = "libHarfBuzzSharp",
            Sdk = sdk,
            Arch = arch,
            Configuration = "Release",
        });

        // copy libHarfBuzzSharp to output
        EnsureDirectoryExists ("native-builds/lib/tvos/" + arch);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_tvos/build/Release-" + sdk + "/libHarfBuzzSharp.a", "native-builds/lib/tvos/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libHarfBuzzSharp.a",
            WorkingDirectory = "native-builds/lib/tvos/" + arch,
        });
    });

    buildHarfBuzzArch ("appletvsimulator", "x86_64");
    buildHarfBuzzArch ("appletvos", "arm64");
    
    // create the fat framework
    RunLipo ("native-builds/lib/tvos/", "libHarfBuzzSharp.a", new [] {
        (FilePath) "x86_64/libHarfBuzzSharp.a", 
        (FilePath) "arm64/libHarfBuzzSharp.a"
    });
});

// this builds the native C and C++ externals for watchOS
Task ("externals-watchos")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnMac ())
    .Does (() => 
{
    // SkiaSharp

    var buildArch = new Action<string, string, string> ((sdk, arch, skiaArch) => {
        var specifics = "";
        // several instances of "error: type 'XXX' requires 8 bytes of alignment and the default allocator only guarantees 4 bytes [-Werror,-Wover-aligned]
        // https://groups.google.com/forum/#!topic/skia-discuss/hU1IPFwU6bI
        if (arch == "armv7k") {
            specifics += ", \"-Wno-over-aligned\"";
        }

        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn"), new ProcessSettings {
            Arguments = 
                "gen out/watchos/" + arch + " " + 
                "--args='" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\"watchos\" target_cpu=\"" + skiaArch + "\"" +
                "  skia_enable_gpu=false" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ \"-DSK_BUILD_FOR_WATCHOS\", \"-DSKIA_C_DLL\", \"-mwatchos-version-min=2.0\" " + specifics + " ]" +
                "  extra_ldflags=[ \"-Wl,watchos_version_min=2.0\" ]" +
                "'",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja"), new ProcessSettings {
            Arguments = "-C out/watchos/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libSkiaSharp_watchos/libSkiaSharp.xcodeproj",
            Target = "libSkiaSharp",
            Sdk = sdk,
            Arch = arch,
            Configuration = "Release",
        });

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/watchos/" + arch);
        CopyDirectory ("native-builds/libSkiaSharp_watchos/build/Release-" + sdk, "native-builds/lib/watchos/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libSkiaSharp",
            WorkingDirectory = "native-builds/lib/watchos/" + arch + "/libSkiaSharp.framework",
        });

        // re-sign with empty
        RunProcess ("codesign", new ProcessSettings {
            Arguments = "--force --sign - --timestamp=none libSkiaSharp.framework",
            WorkingDirectory = "native-builds/lib/watchos/" + arch,
        });
    });

    buildArch ("watchsimulator", "i386", "x86");
    buildArch ("watchos", "armv7k", "arm");

    // create the fat framework
    CopyDirectory ("native-builds/lib/watchos/armv7k/libSkiaSharp.framework/", "native-builds/lib/watchos/libSkiaSharp.framework/");
    DeleteFile ("native-builds/lib/watchos/libSkiaSharp.framework/libSkiaSharp");
    RunLipo ("native-builds/lib/watchos/", "libSkiaSharp.framework/libSkiaSharp", new [] {
        (FilePath) "i386/libSkiaSharp.framework/libSkiaSharp", 
        (FilePath) "armv7k/libSkiaSharp.framework/libSkiaSharp"
    });

    // HarfBuzzSharp

    var buildHarfBuzzArch = new Action<string, string> ((sdk, arch) => {
        // build libHarfBuzzSharp
        XCodeBuild (new XCodeBuildSettings {
            Project = "native-builds/libHarfBuzzSharp_watchos/libHarfBuzzSharp.xcodeproj",
            Target = "libHarfBuzzSharp",
            Sdk = sdk,
            Arch = arch,
            Configuration = "Release",
        });

        // copy libHarfBuzzSharp to output
        EnsureDirectoryExists ("native-builds/lib/watchos/" + arch);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_watchos/build/Release-" + sdk + "/libHarfBuzzSharp.a", "native-builds/lib/watchos/" + arch);

        // strip anything we can
        RunProcess ("strip", new ProcessSettings {
            Arguments = "-x -S libHarfBuzzSharp.a",
            WorkingDirectory = "native-builds/lib/watchos/" + arch,
        });
    });

    buildHarfBuzzArch ("watchsimulator", "i386");
    buildHarfBuzzArch ("watchos", "armv7k");
    
    // create the fat framework
    RunLipo ("native-builds/lib/watchos/", "libHarfBuzzSharp.a", new [] {
        (FilePath) "i386/libHarfBuzzSharp.a", 
        (FilePath) "armv7k/libHarfBuzzSharp.a"
    });
});

// this builds the native C and C++ externals for Android
Task ("externals-android")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnMac ())
    .Does (() => 
{
    // SkiaSharp

    var buildArch = new Action<string, string> ((arch, skiaArch) => {
        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn"), new ProcessSettings {
            Arguments = 
                "gen out/android/" + arch + " " + 
                "--args='" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\"android\" target_cpu=\"" + skiaArch + "\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_freetype2=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  extra_cflags=[ \"-DSKIA_C_DLL\" ]" +
                "  ndk=\"" + ANDROID_NDK_HOME + "\"" + 
                "  ndk_api=" + (skiaArch == "x64" || skiaArch == "arm64" ? 21 : 9) +
                "'",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja"), new ProcessSettings {
            Arguments = "-C out/android/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });
    });

    buildArch ("x86", "x86");
    buildArch ("x86_64", "x64");
    buildArch ("armeabi-v7a", "arm");
    buildArch ("arm64-v8a", "arm64");

    // build libSkiaSharp
    var ndkbuild = MakeAbsolute (Directory (ANDROID_NDK_HOME)).CombineWithFilePath ("ndk-build").FullPath;
    RunProcess (ndkbuild, new ProcessSettings {
        Arguments = "",
        WorkingDirectory = ROOT_PATH.Combine ("native-builds/libSkiaSharp_android").FullPath,
    }); 

    // copy libSkiaSharp to output
    foreach (var folder in new [] { "x86", "x86_64", "armeabi-v7a", "arm64-v8a" }) {
        EnsureDirectoryExists("native-builds/lib/android/" + folder);
        CopyFileToDirectory ("native-builds/libSkiaSharp_android/libs/" + folder + "/libSkiaSharp.so", "native-builds/lib/android/" + folder);
    }

    // HarfBuzzSharp

    // build libHarfBuzzSharp
    RunProcess (ndkbuild, new ProcessSettings {
        Arguments = "",
        WorkingDirectory = ROOT_PATH.Combine ("native-builds/libHarfBuzzSharp_android").FullPath,
    }); 

    // copy libSkiaSharp to output
    foreach (var folder in new [] { "x86", "x86_64", "armeabi-v7a", "arm64-v8a" }) {
        EnsureDirectoryExists ("native-builds/lib/android/" + folder);
        CopyFileToDirectory ("native-builds/libHarfBuzzSharp_android/libs/" + folder + "/libHarfBuzzSharp.so", "native-builds/lib/android/" + folder);
    }
});

// this builds the native C and C++ externals for Linux
Task ("externals-linux")
    .IsDependentOn ("externals-init")
    .WithCriteria (IsRunningOnLinux ())
    .Does (() => 
{
    var arches = EnvironmentVariable ("BUILD_ARCH") ?? (Environment.Is64BitOperatingSystem ? "x64" : "x86");  // x64, x86, ARM
    var BUILD_ARCH = arches.Split (',').Select (a => a.Trim ()).ToArray ();
    var SUPPORT_GPU = (EnvironmentVariable ("SUPPORT_GPU") ?? "1") == "1"; // 1 == true, 0 == false

    var buildArch = new Action<string> ((arch) => {
        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath("bin/gn"), new ProcessSettings {
            Arguments = 
                "gen out/linux/" + arch + " " + 
                "--args='" +
                "  is_official_build=true skia_enable_tools=false" +
                "  target_os=\"linux\" target_cpu=\"" + arch + "\"" +
                "  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                "  skia_use_system_expat=false skia_use_system_freetype2=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                "  skia_enable_gpu=" + (SUPPORT_GPU ? "true" : "false") +
                "  extra_cflags=[ \"-DSKIA_C_DLL\" ]" +
                "  extra_ldflags=[ ]" +
                "'",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (DEPOT_PATH.CombineWithFilePath ("ninja"), new ProcessSettings {
            Arguments = "-C out/linux/" + arch,
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        // RunProcess ("make", new ProcessSettings {
        //     Arguments = "clean",
        //     WorkingDirectory = "native-builds/libSkiaSharp_linux",
        // });
        RunProcess ("make", new ProcessSettings {
            Arguments = "ARCH=" + arch + " VERSION=" + VERSION_FILE + " SUPPORT_GPU=" + SUPPORT_GPU,
            WorkingDirectory = "native-builds/libSkiaSharp_linux",
        });

        // copy libSkiaSharp to output
        EnsureDirectoryExists ("native-builds/lib/linux/" + arch);
        var so = "native-builds/libSkiaSharp_linux/bin/" + arch + "/libSkiaSharp.so." + VERSION_SONAME;
        CopyFileToDirectory (so, "native-builds/lib/linux/" + arch);
        CopyFile (so, "native-builds/lib/linux/" + arch + "/libSkiaSharp.so");
    });

    var buildHarfBuzzArch = new Action<string> ((arch) => {
        // build libHarfBuzzSharp
        // RunProcess ("make", new ProcessSettings {
        //     Arguments = "clean",
        //     WorkingDirectory = "native-builds/libHarfBuzzSharp_linux",
        // });
        RunProcess ("make", new ProcessSettings {
            Arguments = "ARCH=" + arch + " VERSION=" + HARFBUZZ_VERSION_FILE,
            WorkingDirectory = "native-builds/libHarfBuzzSharp_linux",
        });

        // copy libHarfBuzzSharp to output
        EnsureDirectoryExists ("native-builds/lib/linux/" + arch);
        var so = "native-builds/libHarfBuzzSharp_linux/bin/" + arch + "/libHarfBuzzSharp.so." + HARFBUZZ_VERSION_SONAME;
        CopyFileToDirectory (so, "native-builds/lib/linux/" + arch);
        CopyFile (so, "native-builds/lib/linux/" + arch + "/libHarfBuzzSharp.so");
    });

    foreach (var arch in BUILD_ARCH) {
        buildArch (arch);
        buildHarfBuzzArch (arch);
    }
});

// this builds the native C and C++ externals for tizen
Task ("externals-tizen")
    // .IsDependentOn ("externals-init")
    // .WithCriteria (IsRunningOnLinux ())
    .Does (() => 
{
    var buildArch = new Action<string, string, string, string> ((sdk, arch, skiaArch, tizenArch) => {
        var tizenDir = (DirectoryPath) TIZEN_SDK_HOME;
        var gnueabi = arch.EndsWith ("64") ? "gnu" : "gnueabi";
        var cc = tizenDir.Combine ($"tools/{arch}-linux-{gnueabi}-gcc-4.9/bin/{arch}-linux-{gnueabi}-gcc.exe").FullPath;
        var cxx = tizenDir.Combine ($"tools/{arch}-linux-{gnueabi}-gcc-4.9/bin/{arch}-linux-{gnueabi}-g++.exe").FullPath;
        var ar = tizenDir.Combine ($"tools/{arch}-linux-{gnueabi}-gcc-4.9/bin/{arch}-linux-{gnueabi}-ar.exe").FullPath;
        var includeDir = tizenDir.Combine ($"platforms/tizen-3.0/mobile/rootstraps/mobile-3.0-{sdk}.core/usr/include").FullPath;
        var libDir = tizenDir.Combine ($"platforms/tizen-3.0/mobile/rootstraps/mobile-3.0-{sdk}.core/usr/lib").FullPath;

        // generate native skia build files
        RunProcess (SKIA_PATH.CombineWithFilePath ("bin/gn"), new ProcessSettings {
            Arguments = 
                $"gen out/tizen/{tizenArch} " + 
                $"--args=\"" +
                $"  cc=\\\"{cc}\\\" " +
                $"  cxx=\\\"{cxx}\\\" " +
                $"  ar=\\\"{ar}\\\" " +
                $"  is_official_build=true skia_enable_tools=false" +
                $"  target_os=\\\"linux\\\" target_cpu=\\\"{skiaArch}\\\"" +
                $"  skia_use_icu=false skia_use_sfntly=false skia_use_piex=true" +
                $"  skia_use_system_expat=false skia_use_system_freetype2=false skia_use_system_libjpeg_turbo=false skia_use_system_libpng=false skia_use_system_libwebp=false skia_use_system_zlib=false" +
                $"  skia_enable_gpu=true" +
                $"  extra_cflags=[ \\\"-Wno-unused-function\\\", \\\"-DSKIA_C_DLL\\\", \\\"-I{includeDir}\\\" ]" +
                $"  extra_ldflags=[ \\\"-L{libDir}\\\" ]" +
                $"\"",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build native skia
        RunProcess (tizenDir.CombineWithFilePath ("tools/ninja.exe"), new ProcessSettings {
            Arguments = $"-C out/tizen/{tizenArch}",
            WorkingDirectory = SKIA_PATH.FullPath,
        });

        // build libSkiaSharp
        RunProcess (tizenDir.CombineWithFilePath ("tools/ide/bin/tizen.bat"), new ProcessSettings {
            Arguments = $"build-native -a {skiaArch} -c gcc -C Release",
            WorkingDirectory = "native-builds/libSkiaSharp_tizen",
        });

        // copy libSkiaSharp to output
        EnsureDirectoryExists ($"native-builds/lib/tizen/{skiaArch}");
        var so = $"native-builds/libSkiaSharp_tizen/Release/libskiasharp.so";
        CopyFileToDirectory (so, $"native-builds/lib/tizen/{skiaArch}");
        CopyFile (so, $"native-builds/lib/tizen/{skiaArch}/libSkiaSharp.so");
    });

    buildArch ("emulator", "i386", "x86", "i386");
    // buildArch ("emulator", "x86_64", "x64");
    buildArch ("device", "arm", "arm", "armel");
    // buildArch ("device", "aarch64", "arm64");
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// EXTERNALS DOWNLOAD - download any externals that are needed
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("externals-angle-uwp")
    .WithCriteria (!FileExists (ANGLE_PATH.CombineWithFilePath ("uwp/ANGLE.WindowsStore.nuspec")))
    .Does (() =>  
{
    var angleUrl = "https://www.nuget.org/api/v2/package/ANGLE.WindowsStore/" + ANGLE_VERSION_SOURCE;
    var angleRoot = ANGLE_PATH.Combine ("uwp");
    var angleNupkg = angleRoot.CombineWithFilePath ("angle_" + ANGLE_VERSION_SOURCE + ".nupkg");

    EnsureDirectoryExists (angleRoot);
    CleanDirectory (angleRoot);

    DownloadFile (angleUrl, angleNupkg);
    Unzip (angleNupkg, angleRoot);
});

Task ("externals-harfbuzz")
    .WithCriteria (
        !FileExists (HARFBUZZ_PATH.CombineWithFilePath ("harfbuzz/README")) || 
        !FileExists (HARFBUZZ_PATH.CombineWithFilePath ("harfbuzz-" + HARFBUZZ_VERSION_SOURCE + ".tar.bz2")))
    .Does (() =>  
{
    string url = "https://github.com/behdad/harfbuzz/releases/download/" + HARFBUZZ_VERSION_SOURCE + "/harfbuzz-" + HARFBUZZ_VERSION_SOURCE + ".tar.bz2";
    DirectoryPath root = HARFBUZZ_PATH;
    FilePath archive = root.CombineWithFilePath ("harfbuzz-" + HARFBUZZ_VERSION_SOURCE + ".tar.bz2");

    EnsureDirectoryExists (root);
    CleanDirectory (root);

    DownloadFile (url, archive);
    DecompressArchive (archive, root);
    MoveDirectory (root.Combine ("harfbuzz-" + HARFBUZZ_VERSION_SOURCE), HARFBUZZ_PATH.Combine ("harfbuzz"));

    if (IsRunningOnWindows ()) {
        // copy the default config header file
        CopyFile ("externals/harfbuzz/harfbuzz/win32/config.h.win32", "externals/harfbuzz/harfbuzz/win32/config.h");
    } else {
        RunProcess ("bash", new ProcessSettings {
            Arguments = "configure",
            WorkingDirectory = HARFBUZZ_PATH.Combine ("harfbuzz"),
        });
    }
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// CLEAN - remove all the build artefacts
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("externals-deinit").Does (() =>
{
    // remove compatibility
    InjectCompatibilityExternals (false);
});

Task ("clean-externals")
    .IsDependentOn ("externals-deinit")
    .Does (() =>
{
    // skia
    CleanDirectories ("externals/skia/out");
    CleanDirectories ("externals/skia/xcodebuild");

    // harfbuzz
    CleanDirectories ("externals/harfbuzz");

    // angle
    CleanDirectories ("externals/angle");

    // all
    CleanDirectories ("native-builds/lib");
    // android
    CleanDirectories ("native-builds/libSkiaSharp_android/obj");
    CleanDirectories ("native-builds/libSkiaSharp_android/libs");
    // ios
    CleanDirectories ("native-builds/libSkiaSharp_ios/build");
    CleanDirectories ("native-builds/libHarfBuzzSharp_ios/build");
    // tvos
    CleanDirectories ("native-builds/libSkiaSharp_tvos/build");
    CleanDirectories ("native-builds/libHarfBuzzSharp_tvos/build");
    // watchos
    CleanDirectories ("native-builds/libSkiaSharp_watchos/build");
    CleanDirectories ("native-builds/libHarfBuzzSharp_watchos/build");
    // osx
    CleanDirectories ("native-builds/libSkiaSharp_osx/build");
    CleanDirectories ("native-builds/libHarfBuzzSharp_osx/build");
    // windows
    CleanDirectories ("native-builds/libSkiaSharp_windows/bin");
    CleanDirectories ("native-builds/libSkiaSharp_windows/obj");
    CleanDirectories ("native-builds/libHarfBuzzSharp_windows/bin");
    CleanDirectories ("native-builds/libHarfBuzzSharp_windows/obj");
    // uwp
    CleanDirectories ("native-builds/libSkiaSharp_uwp/bin");
    CleanDirectories ("native-builds/libSkiaSharp_uwp/obj");
    CleanDirectories ("native-builds/libHarfBuzzSharp_uwp/bin");
    CleanDirectories ("native-builds/libHarfBuzzSharp_uwp/obj");
    // linux
    CleanDirectories ("native-builds/libSkiaSharp_linux/bin");
    CleanDirectories ("native-builds/libSkiaSharp_linux/obj");
});
