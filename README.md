# elementis-unity

Unity source for the Elementis Android proof build.

- Unity 6000.0.85f2, URP 17.0.3, package `com.elementis.proof`
- Builds an Android APK (IL2CPP, ARM64, target SDK 34) via GitHub Actions
  (`game-ci/unity-builder` v6) — see `.github/workflows/build-android.yml`.
- The build method is `ProofBuild.BuildAndroid` (`Assets/Editor/ProofBuild.cs`),
  output `Builds/Android/elementis-proof.apk`.

Activation: the workflow needs the repository secrets `UNITY_EMAIL` and
`UNITY_PASSWORD` (a Unity ID with a Personal seat; online activation).
No license file is used — Unity removed offline activation for Personal.

This is a proof project. The real Elementis game source lives elsewhere.
