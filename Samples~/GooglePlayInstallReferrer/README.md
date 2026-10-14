# Google Play 설치 Referrer 연동

명시적으로 선택하는 Android 연동입니다. SDK는 Google 라이브러리나 Gradle 설정을 자동으로 설치·변경하지 않습니다.

1. 이 샘플의 `Plugins/Android/InstallReferrerBridge.java`를 게임의 `Assets/Plugins/Android/`에 복사합니다. Unity의 Android Java 소스 플러그인으로 빌드에 포함합니다.
2. Player Settings에서 Android Custom Main Gradle Template을 켜고 생성된 `mainTemplate.gradle`의 `dependencies` 블록에 다음 한 줄을 넣습니다. 기존 Unity 템플릿 치환자는 유지합니다.

```gradle
implementation 'com.android.installreferrer:installreferrer:2.2'
```

3. Google Maven 저장소(`google()`)가 기존 Gradle 프로젝트에 있는지 확인합니다.
4. R8/ProGuard minify를 켠 게임은 custom ProGuard 파일에 다음 규칙을 추가합니다. JNI 호출되는 클래스와 메서드 이름이 유지돼야 합니다.

```proguard
-keep class com.pludibase.acquisition.InstallReferrerBridge { public static void fetch(...); }
-keep class com.android.installreferrer.** { *; }
```

5. `AcquisitionOptions.CollectGooglePlayReferrer = true`로 초기화합니다. 성공한 설치는 다시 읽지 않으며 전송 실패는 HTTP 큐가 원래 증거와 발생시각을 재전송합니다. 서비스 연결 실패·끊김·타임아웃은 제한된 backoff로 다시 연결합니다. 미지원·권한 거부·bridge 누락은 해당 실행에서 unavailable 상태로 남습니다.

Java 콜백은 요청 nonce, 응답 코드, 오류 상태, raw Referrer와 Google의 클릭·설치 시작 초 단위 시각을 보존합니다. nonce가 다른 오래된 콜백은 폐기합니다. Java 10초/C# 15초 타임아웃과 연결 종료를 적용합니다. raw Referrer를 로그나 일반 CSV에 출력하지 마세요. 이 값 자체로 Meta 소재 귀속을 확정하지 않습니다.

Editor, 직접 APK 설치, Google Play가 없는 기기는 검증 환경이 아닙니다. 최종 검수에는 테스트 트랙의 실제 Play 설치가 필요합니다. 이 구현에서는 Unity 빌드·기기·실광고 경로를 실행하지 않았습니다.

공식 근거(2026-10-08 확인): [Google Play Install Referrer Library](https://developer.android.com/google/play/installreferrer/library), [ReferrerDetails](https://developer.android.com/reference/com/android/installreferrer/api/ReferrerDetails), [2.2 릴리스 노트](https://developer.android.com/google/play/installreferrer/release-notes). Google 문서는 설치 후 첫 실행에서 한 번 조회하고 사용 후 `endConnection()`으로 닫도록 안내합니다.
