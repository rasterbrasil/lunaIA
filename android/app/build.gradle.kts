plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.rasterbrasil.luna"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.rasterbrasil.luna"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
    }
}

kotlinOptions {
    jvmTarget = "17"
}
