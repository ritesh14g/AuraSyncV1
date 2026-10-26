package com.aurasync.files;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Android's own file picker (ACTION_OPEN_DOCUMENT) for AuraSync's "Import recording".
 *
 * Unity calls pick(activity, folder); a headless Fragment opens the picker and receives the
 * result, so no Activity or manifest entry is needed. The chosen file is copied into the given
 * folder (the app's own storage) on a background thread. Unity then polls takeResult() each frame:
 *   null           still picking or copying
 *   ""             the person cancelled
 *   "!message"     something failed
 *   anything else  the path of the copy
 */
public class AuraFilePicker {
    private static final int REQUEST = 0x4A51;
    private static final String TAG = "AuraFilePicker";

    private static volatile boolean sBusy;
    private static volatile String sResult;

    public static void pick(final Activity activity, final String destDir) {
        sBusy = true;
        sResult = null;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    FragmentManager fm = activity.getFragmentManager();
                    PickerFragment f = new PickerFragment();
                    Bundle args = new Bundle();
                    args.putString("dest", destDir);
                    f.setArguments(args);
                    fm.beginTransaction().add(f, TAG).commitAllowingStateLoss();
                } catch (Throwable t) {
                    finish("!" + t.getMessage());
                }
            }
        });
    }

    public static boolean isBusy() { return sBusy; }

    /** The outcome once, then null again. */
    public static String takeResult() {
        if (sBusy) return null;
        String r = sResult;
        sResult = null;
        return r;
    }

    static void finish(String result) {
        sResult = result;
        sBusy = false;
    }

    /** Copies what the picker returned into destDir, keeping its file name. Returns the new path. */
    static String copy(Activity activity, Uri uri, String destDir) {
        String name = "import.aurarec";
        Cursor c = null;
        try {
            c = activity.getContentResolver().query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null);
            if (c != null && c.moveToFirst()) {
                String n = c.getString(0);
                if (n != null && n.trim().length() > 0) name = n.trim();
            }
        } catch (Throwable ignored) {
        } finally {
            if (c != null) c.close();
        }
        name = name.replaceAll("[\\\\/:*?\"<>|]", "_");

        File dir = new File(destDir);
        if (!dir.exists() && !dir.mkdirs()) return "!Cannot create " + destDir;
        File out = new File(dir, name);

        InputStream in = null;
        OutputStream os = null;
        try {
            in = activity.getContentResolver().openInputStream(uri);
            if (in == null) return "!The file could not be opened.";
            os = new FileOutputStream(out);
            byte[] buf = new byte[64 * 1024];
            int n;
            while ((n = in.read(buf)) > 0) os.write(buf, 0, n);
            os.flush();
            return out.getAbsolutePath();
        } catch (Throwable t) {
            return "!" + t.getMessage();
        } finally {
            try { if (in != null) in.close(); } catch (Throwable ignored) { }
            try { if (os != null) os.close(); } catch (Throwable ignored) { }
        }
    }

    public static class PickerFragment extends Fragment {
        @Override public void onCreate(Bundle saved) {
            super.onCreate(saved);
            if (saved != null) return; // recreated after a configuration change: the picker is already up
            Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType("*/*"); // WhatsApp and Drive label .aurarec as a generic file
            try {
                startActivityForResult(i, REQUEST);
            } catch (Throwable t) {
                done("!" + t.getMessage());
            }
        }

        @Override public void onActivityResult(int request, int result, Intent data) {
            if (request != REQUEST) return;
            final Activity activity = getActivity();
            if (result != Activity.RESULT_OK || data == null || data.getData() == null || activity == null) {
                done("");
                return;
            }
            final Uri uri = data.getData();
            final String dest = getArguments() != null ? getArguments().getString("dest") : null;
            removeSelf();
            new Thread(new Runnable() {
                @Override public void run() {
                    finish(dest == null ? "!No folder to copy into." : copy(activity, uri, dest));
                }
            }).start();
        }

        private void done(String result) {
            removeSelf();
            finish(result);
        }

        private void removeSelf() {
            try {
                if (getFragmentManager() != null) getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            } catch (Throwable ignored) { }
        }
    }
}
