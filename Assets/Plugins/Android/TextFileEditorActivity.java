package com.dfworkshop.daggerfallunityandroid;

import android.annotation.TargetApi;
import android.app.ActionBar;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.Menu;
import android.view.MenuItem;
import android.view.inputmethod.EditorInfo;
import android.window.OnBackInvokedCallback;
import android.window.OnBackInvokedDispatcher;
import android.widget.EditText;
import android.widget.Toast;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.Charset;
import java.nio.charset.CodingErrorAction;

/**
 * A plain text editor for files in the game's data folder, which the Files app offers for them through
 * DataFolderProvider. Headsets and many phones have no text editor, and settings.ini and mod settings are text.
 */
public class TextFileEditorActivity extends Activity {
    private static final int MAX_FILE_SIZE = 2 * 1024 * 1024;
    private static final int MENU_SAVE = 1;
    private static final byte[] UTF8_BOM = { (byte) 0xEF, (byte) 0xBB, (byte) 0xBF };

    private Uri uri;
    private EditText editor;
    private MenuItem saveItem;
    private boolean modified;
    // An OnBackInvokedCallback while there are unsaved changes, on Android 13 and later
    private Object backCallback;

    // How the file was stored, so that saving writes it back the same way
    private Charset charset;
    private boolean hasBom;
    private boolean usesCrLf;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        uri = getIntent().getData();
        if (uri == null || !DataFolderProvider.getAuthority(this).equals(uri.getAuthority())) {
            finishWithMessage("This editor only opens files in the game's data folder.");
            return;
        }

        String text;
        try {
            text = readText();
        } catch (IOException e) {
            finishWithMessage("Couldn't open this file. " + e.getMessage());
            return;
        }

        showTitle();

        editor = new EditText(this);
        editor.setId(android.R.id.edit);
        editor.setGravity(Gravity.TOP | Gravity.START);
        editor.setTypeface(Typeface.MONOSPACE);
        editor.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        editor.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        // Keeps the keyboard from covering the whole screen in landscape
        editor.setImeOptions(EditorInfo.IME_FLAG_NO_EXTRACT_UI);
        editor.setBackground(null);
        editor.setVerticalScrollBarEnabled(true);
        int padding = Math.round(TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, 12, getResources().getDisplayMetrics()));
        editor.setPadding(padding, padding, padding, padding);
        editor.setText(text);
        editor.addTextChangedListener(new TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {
            }

            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
            }

            @Override
            public void afterTextChanged(Editable s) {
                setModified(true);
            }
        });
        setContentView(editor);
    }

    @Override
    public boolean onCreateOptionsMenu(Menu menu) {
        saveItem = menu.add(Menu.NONE, MENU_SAVE, Menu.NONE, "Save");
        saveItem.setShowAsAction(MenuItem.SHOW_AS_ACTION_ALWAYS | MenuItem.SHOW_AS_ACTION_WITH_TEXT);
        saveItem.setEnabled(modified);
        return true;
    }

    @Override
    public boolean onOptionsItemSelected(MenuItem item) {
        if (item.getItemId() == MENU_SAVE) {
            save();
            return true;
        }
        if (item.getItemId() == android.R.id.home) {
            close();
            return true;
        }
        return super.onOptionsItemSelected(item);
    }

    // Android 16 no longer calls this for apps targeting it; backCallback covers that
    @Override
    public void onBackPressed() {
        if (modified) {
            close();
        } else {
            super.onBackPressed();
        }
    }

    // Asks whether to save first if there are unsaved changes
    private void close() {
        if (!modified) {
            finish();
            return;
        }

        new AlertDialog.Builder(this)
                .setMessage("Save changes to " + getTitle() + "?")
                .setPositiveButton("Save", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        if (save()) {
                            finish();
                        }
                    }
                })
                .setNegativeButton("Discard", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        finish();
                    }
                })
                .setNeutralButton("Cancel", null)
                .show();
    }

    // Shows the file name, with its folder inside the data folder underneath
    private void showTitle() {
        String documentId;
        try {
            documentId = DocumentsContract.getDocumentId(uri);
        } catch (IllegalArgumentException e) {
            documentId = uri.getLastPathSegment();
        }

        int slash = documentId.lastIndexOf('/');
        setTitle(documentId.substring(slash + 1));

        ActionBar actionBar = getActionBar();
        if (actionBar != null) {
            actionBar.setDisplayHomeAsUpEnabled(true);
            int firstSlash = documentId.indexOf('/');
            if (firstSlash >= 0 && slash > firstSlash) {
                actionBar.setSubtitle(documentId.substring(firstSlash + 1, slash));
            }
        }
    }

    private void setModified(boolean value) {
        modified = value;
        if (saveItem != null) {
            saveItem.setEnabled(value);
        }
        if (Build.VERSION.SDK_INT >= 33) {
            updateBackCallback();
        }
    }

    // Catches back only while there are unsaved changes, so that otherwise back closes the editor as usual
    @TargetApi(33)
    private void updateBackCallback() {
        OnBackInvokedDispatcher dispatcher = getOnBackInvokedDispatcher();
        if (modified && backCallback == null) {
            OnBackInvokedCallback callback = new OnBackInvokedCallback() {
                @Override
                public void onBackInvoked() {
                    close();
                }
            };
            dispatcher.registerOnBackInvokedCallback(OnBackInvokedDispatcher.PRIORITY_DEFAULT, callback);
            backCallback = callback;
        } else if (!modified && backCallback != null) {
            dispatcher.unregisterOnBackInvokedCallback((OnBackInvokedCallback) backCallback);
            backCallback = null;
        }
    }

    private String readText() throws IOException {
        byte[] bytes = readBytes();
        for (byte b : bytes) {
            if (b == 0) {
                throw new IOException("It isn't a text file.");
            }
        }

        hasBom = bytes.length >= 3 && bytes[0] == UTF8_BOM[0] && bytes[1] == UTF8_BOM[1] && bytes[2] == UTF8_BOM[2];
        int offset = hasBom ? UTF8_BOM.length : 0;
        String text;
        try {
            charset = Charset.forName("UTF-8");
            text = charset.newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(bytes, offset, bytes.length - offset))
                    .toString();
        } catch (CharacterCodingException e) {
            // Not UTF-8, so likely an older single-byte encoding. Latin-1 keeps every byte as it was.
            charset = Charset.forName("ISO-8859-1");
            hasBom = false;
            text = new String(bytes, charset);
        }

        usesCrLf = text.contains("\r\n");
        return usesCrLf ? text.replace("\r\n", "\n") : text;
    }

    private byte[] readBytes() throws IOException {
        InputStream input = getContentResolver().openInputStream(uri);
        if (input == null) {
            throw new IOException("It couldn't be read.");
        }

        try {
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = input.read(buffer)) != -1) {
                output.write(buffer, 0, read);
                if (output.size() > MAX_FILE_SIZE) {
                    throw new IOException("It's too large to edit here.");
                }
            }
            return output.toByteArray();
        } finally {
            input.close();
        }
    }

    private boolean save() {
        String text = editor.getText().toString();
        if (usesCrLf) {
            text = text.replace("\n", "\r\n");
        }

        try {
            OutputStream output = getContentResolver().openOutputStream(uri, "wt");
            if (output == null) {
                throw new IOException("It couldn't be written.");
            }

            try {
                if (hasBom) {
                    output.write(UTF8_BOM);
                }
                output.write(text.getBytes(charset));
            } finally {
                output.close();
            }
        } catch (IOException e) {
            Toast.makeText(this, "Couldn't save. " + e.getMessage(), Toast.LENGTH_LONG).show();
            return false;
        }

        setModified(false);
        Toast.makeText(this, "Saved. Most changes take effect the next time the game starts.", Toast.LENGTH_LONG).show();
        return true;
    }

    private void finishWithMessage(String message) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show();
        finish();
    }
}
