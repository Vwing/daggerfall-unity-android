package com.dfworkshop.daggerfallunityandroid;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.Environment;
import android.provider.DocumentsContract;
import android.provider.Settings;

import com.unity3d.player.UnityPlayer;

public final class FolderPicker
{
    private FolderPicker() {
    }

    public static void pickFolder(Activity activity, String gameObjectName, String callbackMethodName) {
        ResultFragment fragment = new ResultFragment();
        Bundle args = new Bundle();
        args.putString(ResultFragment.EXTRA_GAME_OBJECT_NAME, gameObjectName);
        args.putString(ResultFragment.EXTRA_CALLBACK_METHOD_NAME, callbackMethodName);
        fragment.setArguments(args);
        activity.getFragmentManager().beginTransaction().add(fragment, "DaggerfallUnityFolderPicker").commitAllowingStateLoss();
        activity.getFragmentManager().executePendingTransactions();
    }

    public static boolean hasAllFilesAccess() {
        if (android.os.Build.VERSION.SDK_INT < 30) {
            return true;
        }

        return Environment.isExternalStorageManager();
    }

    public static void openAllFilesAccessSettings(Activity activity) {
        if (activity == null) {
            return;
        }

        Intent intent;
        if (android.os.Build.VERSION.SDK_INT >= 30) {
            intent = new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION);
            intent.setData(Uri.parse("package:" + activity.getPackageName()));
        } else {
            intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            intent.setData(Uri.parse("package:" + activity.getPackageName()));
        }

        activity.startActivity(intent);
    }

    public static void openFolder(Activity activity, String path) {
        if (activity == null || path == null || path.length() == 0) {
            return;
        }

        Uri uri = getTreeUriFromPath(path);
        if (uri == null) {
            return;
        }

        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(uri, "vnd.android.document/directory");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);

        try {
            activity.startActivity(intent);
        } catch (Exception ex) {
            openFolderPickerAt(activity, uri);
        }
    }

    private static void openFolderPickerAt(Activity activity, Uri uri) {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION
                | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);

        if (android.os.Build.VERSION.SDK_INT >= 26) {
            intent.putExtra(DocumentsContract.EXTRA_INITIAL_URI, uri);
        }

        try {
            activity.startActivity(intent);
        } catch (Exception ignored) {
        }
    }

    private static Uri getTreeUriFromPath(String path) {
        String normalizedPath = path.replace('\\', '/');
        String primaryStoragePath = Environment.getExternalStorageDirectory().getAbsolutePath().replace('\\', '/');
        if (normalizedPath.equals(primaryStoragePath)) {
            return DocumentsContract.buildTreeDocumentUri("com.android.externalstorage.documents", "primary:");
        }

        String primaryStoragePrefix = primaryStoragePath + "/";
        if (normalizedPath.startsWith(primaryStoragePrefix)) {
            String relativePath = normalizedPath.substring(primaryStoragePrefix.length());
            return DocumentsContract.buildTreeDocumentUri("com.android.externalstorage.documents", "primary:" + relativePath);
        }

        String documentsPath = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOCUMENTS).getAbsolutePath().replace('\\', '/');
        if (normalizedPath.equals(documentsPath)) {
            return DocumentsContract.buildTreeDocumentUri("com.android.externalstorage.documents", "home:");
        }

        String documentsPrefix = documentsPath + "/";
        if (normalizedPath.startsWith(documentsPrefix)) {
            String relativePath = normalizedPath.substring(documentsPrefix.length());
            return DocumentsContract.buildTreeDocumentUri("com.android.externalstorage.documents", "home:" + relativePath);
        }

        return null;
    }

    public static class ResultFragment extends Fragment {
        static final String EXTRA_GAME_OBJECT_NAME = "gameObjectName";
        static final String EXTRA_CALLBACK_METHOD_NAME = "callbackMethodName";

        private static final int REQUEST_OPEN_TREE = 44571;

        private String gameObjectName;
        private String callbackMethodName;
        private boolean started;

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            setRetainInstance(true);

            Bundle args = getArguments();
            if (args != null) {
                gameObjectName = args.getString(EXTRA_GAME_OBJECT_NAME);
                callbackMethodName = args.getString(EXTRA_CALLBACK_METHOD_NAME);
            }
        }

        @Override
        public void onStart() {
            super.onStart();

            if (started) {
                return;
            }

            started = true;
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                    | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                    | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION
                    | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
            startActivityForResult(intent, REQUEST_OPEN_TREE);
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            super.onActivityResult(requestCode, resultCode, data);

            String path = "";
            if (requestCode == REQUEST_OPEN_TREE && resultCode == Activity.RESULT_OK && data != null) {
                Uri uri = data.getData();
                if (uri != null) {
                    int flags = data.getFlags() & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
                    try {
                        getActivity().getContentResolver().takePersistableUriPermission(uri, flags);
                    } catch (Exception ignored) {
                    }
                    path = getPathFromTreeUri(uri);
                }
            }

            if (gameObjectName != null && callbackMethodName != null) {
                UnityPlayer.UnitySendMessage(gameObjectName, callbackMethodName, path == null ? "" : path);
            }

            if (getActivity() != null) {
                getActivity().getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            }
        }

        private static String getPathFromTreeUri(Uri uri) {
            if (!DocumentsContract.isTreeUri(uri)) {
                return "";
            }

            String documentId = DocumentsContract.getTreeDocumentId(uri);
            String[] parts = documentId.split(":", 2);
            String volume = parts.length > 0 ? parts[0] : "";
            String relativePath = parts.length > 1 ? parts[1] : "";

            if ("primary".equalsIgnoreCase(volume)) {
                if (relativePath.length() == 0) {
                    return Environment.getExternalStorageDirectory().getAbsolutePath();
                }

                return Environment.getExternalStorageDirectory().getAbsolutePath() + "/" + relativePath;
            }

            if ("home".equalsIgnoreCase(volume)) {
                String documentsPath = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOCUMENTS).getAbsolutePath();
                if (relativePath.length() == 0) {
                    return documentsPath;
                }

                return documentsPath + "/" + relativePath;
            }

            return "";
        }
    }
}
