package com.dfworkshop.daggerfallunityandroid;

import android.annotation.TargetApi;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.graphics.Point;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.provider.DocumentsContract;
import android.provider.DocumentsContract.Document;
import android.provider.DocumentsContract.Root;
import android.provider.DocumentsProvider;
import android.webkit.MimeTypeMap;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayDeque;
import java.util.Arrays;
import java.util.HashMap;
import java.util.LinkedList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * Shows the game's data folder in the system Files app and in other apps' file pickers. Since Android 11 other apps
 * can't reach Android/data, where the data folder is by default, so this is how players get to their mods, saves and
 * settings. Document IDs are "root" for the data folder and "root/" plus a path inside it.
 */
public class DataFolderProvider extends DocumentsProvider {
    private static final String ROOT_ID = "daggerfallunity";
    private static final String ROOT_DOCUMENT_ID = "root";
    // Written by DaggerfallUnityApplication when the player moves the data folder
    private static final String DATA_PATH_LOCATOR = "DaggerfallUnityDataPath.txt";
    private static final int MAX_SEARCH_RESULTS = 100;
    private static final char BYTE_ORDER_MARK = 0xFEFF;

    private static final String[] DEFAULT_ROOT_PROJECTION = {
            Root.COLUMN_ROOT_ID, Root.COLUMN_FLAGS, Root.COLUMN_ICON, Root.COLUMN_TITLE,
            Root.COLUMN_DOCUMENT_ID, Root.COLUMN_AVAILABLE_BYTES
    };
    private static final String[] DEFAULT_DOCUMENT_PROJECTION = {
            Document.COLUMN_DOCUMENT_ID, Document.COLUMN_MIME_TYPE, Document.COLUMN_DISPLAY_NAME,
            Document.COLUMN_LAST_MODIFIED, Document.COLUMN_FLAGS, Document.COLUMN_SIZE
    };

    // Text files the game and its mods use, typed so that text editors are offered for them
    private static final Map<String, String> TEXT_MIME_TYPES = new HashMap<String, String>();
    static {
        for (String extension : new String[] { "txt", "ini", "cfg", "cs", "log", "md", "shader", "cginc", "hlsl", "yml", "yaml" }) {
            TEXT_MIME_TYPES.put(extension, "text/plain");
        }
        TEXT_MIME_TYPES.put("csv", "text/csv");
        TEXT_MIME_TYPES.put("json", "application/json");
        TEXT_MIME_TYPES.put("xml", "text/xml");
    }

    // Image types BitmapFactory can decode, which the Files app shows thumbnails of
    private static final List<String> THUMBNAIL_MIME_TYPES = Arrays.asList("image/png", "image/jpeg", "image/webp", "image/bmp", "image/gif");

    public static String getAuthority(Context context) {
        return context.getPackageName() + ".documents";
    }

    /**
     * The same folder as DaggerfallUnityApplication.PersistentDataPath, worked out here so that the Files app can browse
     * it while the game isn't running.
     */
    public static File getDataFolder(Context context) {
        File appFolder = context.getExternalFilesDir(null);
        if (appFolder == null) {
            appFolder = context.getFilesDir();
        }

        File locator = new File(appFolder, DATA_PATH_LOCATOR);
        if (locator.isFile()) {
            String customPath = readLocator(locator);
            if (customPath.length() > 0) {
                return new File(customPath);
            }
        }

        return new File(appFolder, "DaggerfallUnity");
    }

    /**
     * Opens the Files app at a folder inside the data folder. Returns false if the folder is outside it or no app can
     * show it.
     */
    public static boolean openFolder(Activity activity, String path) {
        String documentId = getDocumentIdForPath(activity, path);
        if (documentId == null) {
            return false;
        }

        String authority = getAuthority(activity);
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(DocumentsContract.buildDocumentUri(authority, documentId), Document.MIME_TYPE_DIR);
        if (tryStartActivity(activity, intent)) {
            return true;
        }

        intent.setDataAndType(DocumentsContract.buildRootUri(authority, ROOT_ID), Root.MIME_TYPE_ITEM);
        return tryStartActivity(activity, intent);
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public Cursor queryRoots(String[] projection) throws FileNotFoundException {
        Context context = getContext();
        File root = getFileForDocumentId(ROOT_DOCUMENT_ID);

        MatrixCursor result = new MatrixCursor(projection != null ? projection : DEFAULT_ROOT_PROJECTION);
        MatrixCursor.RowBuilder row = result.newRow();
        row.add(Root.COLUMN_ROOT_ID, ROOT_ID);
        row.add(Root.COLUMN_DOCUMENT_ID, ROOT_DOCUMENT_ID);
        row.add(Root.COLUMN_TITLE, context.getApplicationInfo().loadLabel(context.getPackageManager()).toString());
        row.add(Root.COLUMN_ICON, context.getApplicationInfo().icon);
        row.add(Root.COLUMN_FLAGS, Root.FLAG_LOCAL_ONLY | Root.FLAG_SUPPORTS_CREATE | Root.FLAG_SUPPORTS_IS_CHILD | Root.FLAG_SUPPORTS_SEARCH);
        row.add(Root.COLUMN_AVAILABLE_BYTES, root.getUsableSpace());
        return result;
    }

    @Override
    public Cursor queryDocument(String documentId, String[] projection) throws FileNotFoundException {
        File file = getFileForDocumentId(documentId);
        if (!file.exists()) {
            throw new FileNotFoundException(documentId + " doesn't exist");
        }

        MatrixCursor result = new MatrixCursor(resolveDocumentProjection(projection));
        includeFile(result, documentId, file);
        return result;
    }

    @Override
    public Cursor queryChildDocuments(String parentDocumentId, String[] projection, String sortOrder) throws FileNotFoundException {
        File folder = getFileForDocumentId(parentDocumentId);

        MatrixCursor result = new MatrixCursor(resolveDocumentProjection(projection));
        File[] children = folder.listFiles();
        if (children != null) {
            for (File child : children) {
                includeFile(result, parentDocumentId + "/" + child.getName(), child);
            }
        }

        Context context = getContext();
        result.setNotificationUri(context.getContentResolver(), DocumentsContract.buildChildDocumentsUri(getAuthority(context), parentDocumentId));
        return result;
    }

    @Override
    public Cursor querySearchDocuments(String rootId, String query, String[] projection) throws FileNotFoundException {
        MatrixCursor result = new MatrixCursor(resolveDocumentProjection(projection));
        if (query == null || query.length() == 0) {
            return result;
        }

        String searchText = query.toLowerCase(Locale.ROOT);
        ArrayDeque<String> pendingIds = new ArrayDeque<String>();
        ArrayDeque<File> pendingFolders = new ArrayDeque<File>();
        pendingIds.add(ROOT_DOCUMENT_ID);
        pendingFolders.add(getFileForDocumentId(ROOT_DOCUMENT_ID));

        while (!pendingFolders.isEmpty()) {
            String folderId = pendingIds.removeFirst();
            File[] children = pendingFolders.removeFirst().listFiles();
            if (children == null) {
                continue;
            }

            for (File child : children) {
                String childId = folderId + "/" + child.getName();
                if (child.getName().toLowerCase(Locale.ROOT).contains(searchText)) {
                    includeFile(result, childId, child);
                    if (result.getCount() >= MAX_SEARCH_RESULTS) {
                        return result;
                    }
                }

                if (child.isDirectory() && !isSymlink(child)) {
                    pendingIds.add(childId);
                    pendingFolders.add(child);
                }
            }
        }

        return result;
    }

    @Override
    public String getDocumentType(String documentId) throws FileNotFoundException {
        return getMimeType(getFileForDocumentId(documentId));
    }

    @Override
    public boolean isChildDocument(String parentDocumentId, String documentId) {
        return documentId.equals(parentDocumentId) || documentId.startsWith(parentDocumentId + "/");
    }

    @Override
    @TargetApi(26)
    public DocumentsContract.Path findDocumentPath(String parentDocumentId, String childDocumentId) throws FileNotFoundException {
        String fromId = parentDocumentId != null ? parentDocumentId : ROOT_DOCUMENT_ID;
        if (!isChildDocument(fromId, childDocumentId)) {
            throw new FileNotFoundException(childDocumentId + " isn't inside " + fromId);
        }
        getFileForDocumentId(childDocumentId);

        LinkedList<String> path = new LinkedList<String>();
        String documentId = childDocumentId;
        path.addFirst(documentId);
        while (!documentId.equals(fromId)) {
            documentId = getParentDocumentId(documentId);
            path.addFirst(documentId);
        }

        return new DocumentsContract.Path(parentDocumentId != null ? null : ROOT_ID, path);
    }

    @Override
    public ParcelFileDescriptor openDocument(String documentId, String mode, CancellationSignal signal) throws FileNotFoundException {
        File file = getFileForDocumentId(documentId);
        int accessMode = ParcelFileDescriptor.parseMode(mode);
        if (mode.indexOf('w') < 0) {
            return ParcelFileDescriptor.open(file, accessMode);
        }

        // Tell the Files app once the writer is done, so that it shows the new size
        final String parentDocumentId = getParentDocumentId(documentId);
        try {
            return ParcelFileDescriptor.open(file, accessMode, new Handler(Looper.getMainLooper()), new ParcelFileDescriptor.OnCloseListener() {
                @Override
                public void onClose(IOException e) {
                    notifyChildrenChanged(parentDocumentId);
                }
            });
        } catch (IOException e) {
            throw asFileNotFound(e);
        }
    }

    @Override
    public AssetFileDescriptor openDocumentThumbnail(String documentId, Point sizeHint, CancellationSignal signal) throws FileNotFoundException {
        File file = getFileForDocumentId(documentId);
        ParcelFileDescriptor descriptor = ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
        return new AssetFileDescriptor(descriptor, 0, AssetFileDescriptor.UNKNOWN_LENGTH);
    }

    @Override
    public String createDocument(String parentDocumentId, String mimeType, String displayName) throws FileNotFoundException {
        File folder = getFileForDocumentId(parentDocumentId);
        if (!folder.isDirectory()) {
            throw new FileNotFoundException(parentDocumentId + " isn't a folder");
        }

        boolean isFolder = Document.MIME_TYPE_DIR.equals(mimeType);
        String name = cleanFileName(displayName);
        if (!isFolder && name.lastIndexOf('.') <= 0 && mimeType != null && !mimeType.equals("application/octet-stream")) {
            String extension = MimeTypeMap.getSingleton().getExtensionFromMimeType(mimeType);
            if (extension != null) {
                name += "." + extension;
            }
        }

        File file = getFreeFile(folder, name, isFolder);
        try {
            boolean created = isFolder ? file.mkdir() : file.createNewFile();
            if (!created) {
                throw new FileNotFoundException("Couldn't create " + file.getName());
            }
        } catch (IOException e) {
            throw asFileNotFound(e);
        }

        notifyChildrenChanged(parentDocumentId);
        return parentDocumentId + "/" + file.getName();
    }

    @Override
    public void deleteDocument(String documentId) throws FileNotFoundException {
        if (ROOT_DOCUMENT_ID.equals(documentId)) {
            throw new FileNotFoundException("The data folder itself can't be deleted");
        }

        File file = getFileForDocumentId(documentId);
        if (!deleteRecursively(file)) {
            throw new FileNotFoundException("Couldn't delete " + file.getName());
        }

        notifyChildrenChanged(getParentDocumentId(documentId));
    }

    @Override
    public String renameDocument(String documentId, String displayName) throws FileNotFoundException {
        if (ROOT_DOCUMENT_ID.equals(documentId)) {
            throw new FileNotFoundException("The data folder itself can't be renamed");
        }

        File file = getFileForDocumentId(documentId);
        String name = cleanFileName(displayName);
        if (name.equals(file.getName())) {
            return null;
        }

        // A change of case alone finds the file itself on case-insensitive storage
        File target = new File(file.getParentFile(), name);
        if (target.exists() && !name.equalsIgnoreCase(file.getName())) {
            throw new FileNotFoundException(name + " already exists");
        }
        if (!file.renameTo(target)) {
            throw new FileNotFoundException("Couldn't rename " + file.getName());
        }

        String parentDocumentId = getParentDocumentId(documentId);
        notifyChildrenChanged(parentDocumentId);
        return parentDocumentId + "/" + name;
    }

    @Override
    @TargetApi(24)
    public String moveDocument(String sourceDocumentId, String sourceParentDocumentId, String targetParentDocumentId) throws FileNotFoundException {
        File source = getFileForDocumentId(sourceDocumentId);
        File targetFolder = getTargetFolder(sourceDocumentId, targetParentDocumentId);
        String sourceParentId = getParentDocumentId(sourceDocumentId);
        if (targetParentDocumentId.equals(sourceParentId)) {
            return sourceDocumentId;
        }

        File target = getFreeFile(targetFolder, source.getName(), source.isDirectory());
        if (!source.renameTo(target)) {
            copyRecursively(source, target);
            deleteRecursively(source);
        }

        notifyChildrenChanged(sourceParentId);
        notifyChildrenChanged(targetParentDocumentId);
        return targetParentDocumentId + "/" + target.getName();
    }

    @Override
    @TargetApi(24)
    public String copyDocument(String sourceDocumentId, String targetParentDocumentId) throws FileNotFoundException {
        File source = getFileForDocumentId(sourceDocumentId);
        File targetFolder = getTargetFolder(sourceDocumentId, targetParentDocumentId);

        File target = getFreeFile(targetFolder, source.getName(), source.isDirectory());
        copyRecursively(source, target);

        notifyChildrenChanged(targetParentDocumentId);
        return targetParentDocumentId + "/" + target.getName();
    }

    private File getFileForDocumentId(String documentId) throws FileNotFoundException {
        File root = getDataFolder(getContext());
        if (ROOT_DOCUMENT_ID.equals(documentId)) {
            if (!root.exists()) {
                root.mkdirs();
            }
            return root;
        }

        if (documentId == null || !documentId.startsWith(ROOT_DOCUMENT_ID + "/")) {
            throw new FileNotFoundException("Unknown document " + documentId);
        }

        String relativePath = documentId.substring(ROOT_DOCUMENT_ID.length() + 1);
        for (String part : relativePath.split("/", -1)) {
            if (part.length() == 0 || part.equals(".") || part.equals("..")) {
                throw new FileNotFoundException("Invalid document " + documentId);
            }
        }

        File file = new File(root, relativePath);
        if (!isInside(root, file)) {
            throw new FileNotFoundException(documentId + " is outside the data folder");
        }
        return file;
    }

    private static String getDocumentIdForPath(Context context, String path) {
        try {
            String rootPath = getDataFolder(context).getCanonicalPath();
            String filePath = new File(path).getCanonicalPath();
            if (filePath.equals(rootPath)) {
                return ROOT_DOCUMENT_ID;
            }
            if (filePath.startsWith(rootPath + "/")) {
                return ROOT_DOCUMENT_ID + filePath.substring(rootPath.length());
            }
        } catch (IOException ignored) {
        }

        return null;
    }

    private static String getParentDocumentId(String documentId) {
        int slash = documentId.lastIndexOf('/');
        return slash < 0 ? null : documentId.substring(0, slash);
    }

    private File getTargetFolder(String sourceDocumentId, String targetParentDocumentId) throws FileNotFoundException {
        if (isChildDocument(sourceDocumentId, targetParentDocumentId)) {
            throw new FileNotFoundException("A folder can't go inside itself");
        }

        File targetFolder = getFileForDocumentId(targetParentDocumentId);
        if (!targetFolder.isDirectory()) {
            throw new FileNotFoundException(targetParentDocumentId + " isn't a folder");
        }
        return targetFolder;
    }

    private void includeFile(MatrixCursor result, String documentId, File file) {
        boolean isFolder = file.isDirectory();
        String mimeType = getMimeType(file);

        int flags = 0;
        if (isFolder) {
            if (file.canWrite()) {
                flags |= Document.FLAG_DIR_SUPPORTS_CREATE;
            }
        } else {
            if (file.canWrite()) {
                flags |= Document.FLAG_SUPPORTS_WRITE;
            }
            if (THUMBNAIL_MIME_TYPES.contains(mimeType)) {
                flags |= Document.FLAG_SUPPORTS_THUMBNAIL;
            }
        }
        if (!ROOT_DOCUMENT_ID.equals(documentId)) {
            flags |= Document.FLAG_SUPPORTS_COPY;
            File parent = file.getParentFile();
            if (parent != null && parent.canWrite()) {
                flags |= Document.FLAG_SUPPORTS_DELETE | Document.FLAG_SUPPORTS_RENAME | Document.FLAG_SUPPORTS_MOVE;
            }
        }

        MatrixCursor.RowBuilder row = result.newRow();
        row.add(Document.COLUMN_DOCUMENT_ID, documentId);
        row.add(Document.COLUMN_DISPLAY_NAME, file.getName());
        row.add(Document.COLUMN_MIME_TYPE, mimeType);
        row.add(Document.COLUMN_SIZE, isFolder ? null : file.length());
        row.add(Document.COLUMN_LAST_MODIFIED, file.lastModified());
        row.add(Document.COLUMN_FLAGS, flags);
    }

    private void notifyChildrenChanged(String parentDocumentId) {
        if (parentDocumentId == null) {
            return;
        }

        Context context = getContext();
        context.getContentResolver().notifyChange(DocumentsContract.buildChildDocumentsUri(getAuthority(context), parentDocumentId), null, false);
    }

    private static String[] resolveDocumentProjection(String[] projection) {
        return projection != null ? projection : DEFAULT_DOCUMENT_PROJECTION;
    }

    private static String getMimeType(File file) {
        if (file.isDirectory()) {
            return Document.MIME_TYPE_DIR;
        }

        String name = file.getName();
        int dot = name.lastIndexOf('.');
        if (dot >= 0) {
            String extension = name.substring(dot + 1).toLowerCase(Locale.ROOT);
            String textType = TEXT_MIME_TYPES.get(extension);
            if (textType != null) {
                return textType;
            }

            String mimeType = MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension);
            if (mimeType != null) {
                return mimeType;
            }
        }

        return "application/octet-stream";
    }

    private static String cleanFileName(String displayName) throws FileNotFoundException {
        String name = displayName == null ? "" : displayName.replace('/', '_').replace('\0', '_').trim();
        if (name.length() == 0 || name.equals(".") || name.equals("..")) {
            throw new FileNotFoundException("Invalid name " + displayName);
        }
        return name;
    }

    // Adds " (1)", " (2)" and so on before the extension until the name is free
    private static File getFreeFile(File folder, String name, boolean isFolder) {
        File file = new File(folder, name);
        if (!file.exists()) {
            return file;
        }

        int dot = isFolder ? -1 : name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name;
        String extension = dot > 0 ? name.substring(dot) : "";
        for (int i = 1; ; i++) {
            file = new File(folder, stem + " (" + i + ")" + extension);
            if (!file.exists()) {
                return file;
            }
        }
    }

    private static boolean isInside(File folder, File file) {
        try {
            String folderPath = folder.getCanonicalPath();
            String filePath = file.getCanonicalPath();
            return filePath.equals(folderPath) || filePath.startsWith(folderPath + "/");
        } catch (IOException e) {
            return false;
        }
    }

    // Symlinked folders are left alone, so that deleting, copying or searching never reaches outside the data folder
    private static boolean isSymlink(File file) {
        try {
            File parent = file.getParentFile();
            if (parent == null) {
                return false;
            }
            File inCanonicalParent = new File(parent.getCanonicalFile(), file.getName());
            return !inCanonicalParent.getCanonicalPath().equals(inCanonicalParent.getAbsolutePath());
        } catch (IOException e) {
            return true;
        }
    }

    private static boolean deleteRecursively(File file) {
        if (file.isDirectory() && !isSymlink(file)) {
            File[] children = file.listFiles();
            if (children != null) {
                for (File child : children) {
                    deleteRecursively(child);
                }
            }
        }
        return file.delete() || !file.exists();
    }

    private static void copyRecursively(File source, File target) throws FileNotFoundException {
        if (source.isDirectory()) {
            if (!target.mkdir() && !target.isDirectory()) {
                throw new FileNotFoundException("Couldn't create " + target.getName());
            }

            File[] children = source.listFiles();
            if (children != null) {
                for (File child : children) {
                    if (!isSymlink(child)) {
                        copyRecursively(child, new File(target, child.getName()));
                    }
                }
            }
            return;
        }

        try {
            InputStream input = new FileInputStream(source);
            try {
                OutputStream output = new FileOutputStream(target);
                try {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = input.read(buffer)) != -1) {
                        output.write(buffer, 0, read);
                    }
                } finally {
                    output.close();
                }
            } finally {
                input.close();
            }
        } catch (IOException e) {
            throw asFileNotFound(e);
        }
        target.setLastModified(source.lastModified());
    }

    private static String readLocator(File locator) {
        try {
            InputStream input = new FileInputStream(locator);
            try {
                ByteArrayOutputStream bytes = new ByteArrayOutputStream();
                byte[] buffer = new byte[4096];
                int read;
                while ((read = input.read(buffer)) != -1) {
                    bytes.write(buffer, 0, read);
                }
                String text = new String(bytes.toByteArray(), "UTF-8");
                if (text.length() > 0 && text.charAt(0) == BYTE_ORDER_MARK) {
                    text = text.substring(1);
                }
                return text.trim();
            } finally {
                input.close();
            }
        } catch (IOException e) {
            return "";
        }
    }

    private static FileNotFoundException asFileNotFound(IOException e) {
        if (e instanceof FileNotFoundException) {
            return (FileNotFoundException) e;
        }
        FileNotFoundException notFound = new FileNotFoundException(e.getMessage());
        notFound.initCause(e);
        return notFound;
    }

    private static boolean tryStartActivity(Activity activity, Intent intent) {
        try {
            activity.startActivity(intent);
            return true;
        } catch (ActivityNotFoundException e) {
            return false;
        } catch (SecurityException e) {
            return false;
        }
    }
}
