import java.io.File

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

/**
 * One version number for every platform: the Windows app's
 * (src/Navigatueur.App/Navigatueur.App.csproj). A release tag vX.Y.Z then
 * ships the Windows installer and these APKs side by side.
 */
val navigatueurVersion: String = run {
    val csproj = rootProject.file("../src/Navigatueur.App/Navigatueur.App.csproj").readText()
    Regex("<Version>(\\d+\\.\\d+\\.\\d+)</Version>").find(csproj)?.groupValues?.get(1)
        ?: error("No <Version> in Navigatueur.App.csproj")
}

val navigatueurVersionCode: Int = navigatueurVersion.split('.').map(String::toInt)
    .let { (major, minor, patch) -> major * 1_000_000 + minor * 1_000 + patch } * 10

/** Release signing comes from the CI's secrets; without them a release build is signed with the debug key. */
val releaseKeystore: String? = System.getenv("NAVIGATUEUR_KEYSTORE")?.takeIf { File(it).isFile }

android {
    namespace = "com.navigatueur.mobile"
    compileSdk {
        version = release(37) { minorApiLevel = 2 }
    }

    defaultConfig {
        applicationId = "com.navigatueur.mobile"
        minSdk = 26
        targetSdk = 37
        versionCode = navigatueurVersionCode
        versionName = navigatueurVersion
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (releaseKeystore != null) {
            create("release") {
                storeFile = File(releaseKeystore)
                storePassword = System.getenv("NAVIGATUEUR_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("NAVIGATUEUR_KEY_ALIAS")
                keyPassword = System.getenv("NAVIGATUEUR_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    // GeckoView carries a whole engine per processor type: one APK per type
    // keeps each download to a single engine instead of three.
    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a", "armeabi-v7a", "x86_64")
            isUniversalApk = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
        jniLibs {
            useLegacyPackaging = true
        }
    }

    testOptions {
        unitTests.isReturnDefaultValues = true
    }
}

// APK names carry the version and the processor type, e.g. Navigatueur-Android-0.22.0-arm64-v8a.apk.
androidComponents {
    onVariants { variant ->
        // The Windows app's phishing list, packed as is (not copied into the Android tree).
        variant.sources.assets?.addStaticSourceDirectory(rootProject.file("../src/Navigatueur.App/Resources/Phishing").path)

        variant.outputs.forEach { output ->
            val abi = output.filters.find { it.filterType.name == "ABI" }?.identifier ?: "universal"
            (output as? com.android.build.api.variant.impl.VariantOutputImpl)?.outputFileName?.set(
                "Navigatueur-Android-$navigatueurVersion-$abi${if (variant.buildType == "debug") "-debug" else ""}.apk"
            )
        }
    }
}

dependencies {
    implementation("org.mozilla.geckoview:geckoview:159.0.20261102144711")

    implementation("androidx.core:core-ktx:1.19.1")
    implementation("androidx.activity:activity-compose:1.9.1")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.11.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.11.0")
    implementation("androidx.lifecycle:lifecycle-process:2.11.0")
    implementation(platform("androidx.compose:compose-bom:2025.01.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.media:media:1.7.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.9.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    debugImplementation("androidx.compose.ui:ui-tooling")

    testImplementation("junit:junit:4.13.2")
}
