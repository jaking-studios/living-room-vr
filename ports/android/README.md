# Living Room VR — Android app

A tiny native Android app (WebView + `androidx.webkit` asset loader) that bundles the whole web game,
so it runs **offline** from the APK with touch controls, gamepads and TV remotes.

Rebuild: double-click `build_apk.bat` (needs Android Studio). It copies the game from the repo root into
`app/src/main/assets/` and runs Gradle; the result is `LivingRoomVR.apk`. Or open this folder in Android Studio.

The APK is signed with the debug key so it can be sideloaded; use your own keystore for Google Play.
