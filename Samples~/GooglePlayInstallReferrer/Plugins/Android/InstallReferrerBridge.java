package com.pludibase.acquisition;

import android.app.Activity;
import android.os.Handler;
import android.os.Looper;
import com.android.installreferrer.api.InstallReferrerClient;
import com.android.installreferrer.api.InstallReferrerStateListener;
import com.android.installreferrer.api.ReferrerDetails;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;
import java.util.concurrent.atomic.AtomicBoolean;

/** Opt-in bridge. The game must explicitly include installreferrer:2.2; no SDK build hooks install it. */
public final class InstallReferrerBridge {
    private InstallReferrerBridge() {}

    public static void fetch(final Activity activity, final String target, final String callback, final String nonce) {
        final Handler handler = new Handler(Looper.getMainLooper());
        final AtomicBoolean completed = new AtomicBoolean(false);
        final InstallReferrerClient[] client = new InstallReferrerClient[1];
        final Runnable[] timeout = new Runnable[1];

        class Completion {
            void finish(String status, String reason, int responseCode, ReferrerDetails details) {
                if (!completed.compareAndSet(false, true)) return;
                if (timeout[0] != null) handler.removeCallbacks(timeout[0]);
                JSONObject result = new JSONObject();
                try {
                    result.put("nonce", nonce);
                    result.put("status", status);
                    result.put("reason", reason);
                    result.put("responseCode", responseCode);
                    if (details != null) {
                        result.put("rawReferrer", details.getInstallReferrer());
                        result.put("referrerClickTimestampSeconds", details.getReferrerClickTimestampSeconds());
                        result.put("installBeginTimestampSeconds", details.getInstallBeginTimestampSeconds());
                        result.put("googlePlayInstant", details.getGooglePlayInstantParam());
                    }
                    UnityPlayer.UnitySendMessage(target, callback, result.toString());
                } catch (Exception ignored) {
                    // This fixed fallback contains neither provider evidence nor credentials.
                    UnityPlayer.UnitySendMessage(target, callback,
                        "{\"nonce\":\"" + nonce + "\",\"status\":\"error\",\"responseCode\":-1,\"reason\":\"Callback serialization failed.\"}");
                } finally {
                    if (client[0] != null) {
                        try { client[0].endConnection(); } catch (Exception ignored) {}
                    }
                }
            }
        }
        final Completion completion = new Completion();
        handler.post(new Runnable() {
            @Override public void run() {
                if (completed.get()) return;
                try {
                    client[0] = InstallReferrerClient.newBuilder(activity.getApplicationContext()).build();
                    timeout[0] = new Runnable() {
                        @Override public void run() { completion.finish("timeout", "Google Play service timed out.", -1, null); }
                    };
                    handler.postDelayed(timeout[0], 10000);
                    client[0].startConnection(new InstallReferrerStateListener() {
                        @Override public void onInstallReferrerSetupFinished(int code) {
                            if (completed.get()) return;
                            if (code == InstallReferrerClient.InstallReferrerResponse.OK) {
                                try {
                                    completion.finish("ok", "", code, client[0].getInstallReferrer());
                                } catch (Exception ex) {
                                    completion.finish("error", "Google Play referrer retrieval failed.", code, null);
                                }
                            } else if (code == InstallReferrerClient.InstallReferrerResponse.FEATURE_NOT_SUPPORTED) {
                                completion.finish("unsupported", "Google Play Install Referrer is unsupported.", code, null);
                            } else if (code == InstallReferrerClient.InstallReferrerResponse.PERMISSION_ERROR) {
                                completion.finish("permission_error", "Google Play refused the service binding.", code, null);
                            } else {
                                completion.finish("service_unavailable", "Google Play service connection failed.", code, null);
                            }
                        }
                        @Override public void onInstallReferrerServiceDisconnected() {
                            // Close this request; the C# module reconnects with bounded backoff on its next attempt.
                            completion.finish("disconnected", "Google Play service disconnected.", -1, null);
                        }
                    });
                } catch (Exception ex) {
                    completion.finish("error", "Install Referrer client setup failed.", -1, null);
                }
            }
        });
    }
}
