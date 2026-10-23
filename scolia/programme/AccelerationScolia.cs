// SCOLIA V80 - noyau d'accélération compilé.
// Les boucles exécutées à chaque frappe, à chaque actualisation de tableau ou à
// chaque période d'inactivité Windows passent ici en C# au lieu d'être interprétées
// par PowerShell. Chaque méthode reproduit exactement la sémantique du code
// PowerShell qu'elle remplace (conversions [string], accès $objet.(nom), égalités).
// Compatibilité : C# 5 / .NET Framework 4 (compilateur d'Add-Type de Windows PowerShell 5.1).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using System.Windows.Forms;

public static class ITMFast
{
    public const string Version = "SCOLIA-FAST-1";

    // ---------- Clés de recherche (équivalent exact de la fonction PowerShell Key) ----------
    private static readonly Dictionary<string, string> keyCache = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly object keyGate = new object();

    public static string Key(string raw)
    {
        if (raw == null) raw = string.Empty;
        string hit;
        lock (keyGate)
        {
            if (keyCache.TryGetValue(raw, out hit)) return hit;
        }
        string t = raw.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(t.Length);
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }
        string result = builder.ToString().Normalize(NormalizationForm.FormC);
        lock (keyGate)
        {
            if (keyCache.Count >= 16384) keyCache.Clear();
            keyCache[raw] = result;
        }
        return result;
    }

    public static void ClearKeyCache()
    {
        lock (keyGate) { keyCache.Clear(); }
    }

    // ---------- Accès aux membres avec la sémantique de PowerShell ----------
    // $entry.(nom) : clé de dictionnaire d'abord, puis propriété (adaptée ou note).
    public static object Member(object entry, string name)
    {
        if (entry == null) return null;
        PSObject wrapped = entry as PSObject;
        object baseObject = wrapped != null ? wrapped.BaseObject : entry;
        IDictionary dictionary = baseObject as IDictionary;
        if (dictionary != null)
        {
            if (dictionary.Contains(name)) return dictionary[name];
        }
        if (wrapped == null) wrapped = PSObject.AsPSObject(entry);
        PSPropertyInfo property = wrapped.Properties[name];
        if (property == null) return null;
        return property.Value;
    }

    // PowerShell transmet l'objet de base aux méthodes .NET, sauf pour un PSCustomObject.
    public static object Unwrap(object value)
    {
        PSObject wrapped = value as PSObject;
        if (wrapped == null) return value;
        if (wrapped.BaseObject is PSCustomObject) return value;
        return wrapped.BaseObject;
    }

    // Conversion [string]$valeur de PowerShell ($null et DBNull donnent une chaîne vide).
    public static string Text(object value)
    {
        if (value == null || value is DBNull) return string.Empty;
        string direct = value as string;
        if (direct != null) return direct;
        object converted = LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
        return converted == null ? string.Empty : (string)converted;
    }

    // ---------- Construction d'une table de grille (boucle interne de Set-Grid) ----------
    public static DataTable BuildTable(object[] rows, string[] properties, string[] names, Type[] types)
    {
        if (properties == null || names == null || types == null) throw new ArgumentNullException("properties");
        if (properties.Length != names.Length || names.Length != types.Length) throw new ArgumentException("Description de colonnes incohérente.");
        DataTable table = new DataTable();
        bool[] isText = new bool[names.Length];
        for (int c = 0; c < names.Length; c++)
        {
            Type columnType = types[c] ?? typeof(string);
            table.Columns.Add(names[c], columnType);
            isText[c] = table.Columns[names[c]].DataType == typeof(string);
        }
        table.BeginLoadData();
        try
        {
            if (rows != null)
            {
                for (int r = 0; r < rows.Length; r++)
                {
                    object entry = rows[r];
                    if (entry == null) continue;
                    DataRow row = table.NewRow();
                    for (int c = 0; c < names.Length; c++)
                    {
                        object value = Member(entry, properties[c]);
                        if (value == null) row[c] = isText[c] ? (object)string.Empty : DBNull.Value;
                        else if (isText[c]) row[c] = Text(value);
                        else row[c] = Unwrap(value);
                    }
                    table.Rows.Add(row);
                }
            }
        }
        finally
        {
            table.EndLoadData();
        }
        return table;
    }

    // ---------- Rapprochement d'une table liée (équivalent de Sync-ScoliaGridTable) ----------
    private static string RowKey(DataRow row, string[] keys)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < keys.Length; i++)
        {
            string value = Text(row[keys[i]]);
            builder.Append(value.Length).Append(':').Append(value);
        }
        return builder.ToString();
    }

    public static bool SyncTable(DataTable target, DataTable source, string[] keys)
    {
        if (target == null || source == null) return false;
        if (keys == null) keys = new string[0];
        if (target.Columns.Count != source.Columns.Count) return false;
        for (int i = 0; i < target.Columns.Count; i++)
        {
            if (!string.Equals(target.Columns[i].ColumnName, source.Columns[i].ColumnName, StringComparison.Ordinal) ||
                target.Columns[i].DataType != source.Columns[i].DataType) return false;
        }
        int columnCount = target.Columns.Count;
        if (keys.Length == 0)
        {
            if (target.Rows.Count != source.Rows.Count) return false;
            for (int i = 0; i < target.Rows.Count; i++)
            {
                for (int j = 0; j < columnCount; j++)
                {
                    if (!object.Equals(target.Rows[i][j], source.Rows[i][j])) return false;
                }
            }
            return true;
        }
        Dictionary<string, DataRow> old = new Dictionary<string, DataRow>(StringComparer.Ordinal);
        Dictionary<string, DataRow> incoming = new Dictionary<string, DataRow>(StringComparer.Ordinal);
        List<string> oldOrder = new List<string>();
        foreach (DataRow row in target.Rows)
        {
            string key = RowKey(row, keys);
            if (old.ContainsKey(key)) return false;
            old.Add(key, row);
            oldOrder.Add(key);
        }
        List<string> incomingKeys = new List<string>(source.Rows.Count);
        foreach (DataRow row in source.Rows)
        {
            string key = RowKey(row, keys);
            if (incoming.ContainsKey(key)) return false;
            incoming.Add(key, row);
            incomingKeys.Add(key);
        }
        foreach (string key in oldOrder)
        {
            if (!incoming.ContainsKey(key)) target.Rows.Remove(old[key]);
        }
        for (int r = 0; r < source.Rows.Count; r++)
        {
            DataRow row = source.Rows[r];
            string key = incomingKeys[r];
            DataRow existing;
            if (!old.TryGetValue(key, out existing))
            {
                target.ImportRow(row);
                continue;
            }
            for (int j = 0; j < columnCount; j++)
            {
                if (!object.Equals(existing[j], row[j])) existing[j] = row[j];
            }
        }
        return true;
    }

    // ---------- Filtre de l'index des apprenants (boucle de Learner-Rows) ----------
    public static object[] FilterLearners(object[] index, string[] terms, bool allPromotions, string promotionKey)
    {
        List<object> result = new List<object>();
        if (index == null) return result.ToArray();
        if (terms == null) terms = new string[0];
        CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
        for (int i = 0; i < index.Length; i++)
        {
            object item = index[i];
            if (item == null) continue;
            if (!allPromotions)
            {
                string promotion = Text(Member(item, "Promotion"));
                if (compare.Compare(promotion, promotionKey ?? string.Empty, CompareOptions.IgnoreCase) != 0) continue;
            }
            bool match = true;
            if (terms.Length > 0)
            {
                string text = Text(Member(item, "Text"));
                for (int t = 0; t < terms.Length; t++)
                {
                    if (text.IndexOf(terms[t], StringComparison.Ordinal) < 0) { match = false; break; }
                }
            }
            if (match) result.Add(Member(item, "Entry"));
        }
        return result.ToArray();
    }
}

// ===== WINFORMS ===== (parties dépendantes de Windows Forms)
public static class ITMFastGrid
{
    // ---------- Réselection d'un dossier après actualisation (fin de Set-Grid) ----------
    // Renvoie l'index de la seule ligne correspondante, -1 si aucune ou plusieurs,
    // -2 si la grille n'est pas liée de façon standard (le code PowerShell prend alors le relais).
    public static int FindUniqueRow(DataGridView grid, string[] names, string[] values)
    {
        if (grid == null || names == null || values == null || names.Length != values.Length) return -2;
        if (grid.BindingContext == null || grid.DataSource == null) return -2;
        CurrencyManager manager = grid.BindingContext[grid.DataSource, grid.DataMember] as CurrencyManager;
        if (manager == null) return -2;
        IList list = manager.List;
        if (list == null || list.Count != grid.Rows.Count) return -2;
        string[] members = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            if (!grid.Columns.Contains(names[i])) return -2;
            string member = grid.Columns[names[i]].DataPropertyName;
            if (string.IsNullOrEmpty(member)) return -2;
            members[i] = member;
        }
        int match = -1, matches = 0;
        for (int r = 0; r < list.Count; r++)
        {
            DataRowView view = list[r] as DataRowView;
            if (view == null) return -2;
            bool equal = true;
            for (int i = 0; i < members.Length; i++)
            {
                if (!view.Row.Table.Columns.Contains(members[i])) return -2;
                if (!string.Equals(ITMFast.Text(view[members[i]]), values[i], StringComparison.Ordinal)) { equal = false; break; }
            }
            if (equal) { match = r; matches++; }
        }
        return matches == 1 ? match : -1;
    }
}

// Surveille l'ouverture de nouvelles fenêtres pendant les périodes d'inactivité de
// Windows. L'ancien gestionnaire PowerShell s'exécutait à chaque événement Idle
// (après chaque mouvement de souris ou frappe). Ici, le test est fait en C# et
// PowerShell n'est appelé que lorsqu'une fenêtre n'est pas encore enregistrée.
public sealed class ITMFormWatcher
{
    private readonly IDictionary registry;
    private bool busy;
    private bool attached;

    public event EventHandler FormNeedsTheme;

    public ITMFormWatcher(IDictionary registry)
    {
        if (registry == null) throw new ArgumentNullException("registry");
        this.registry = registry;
    }

    public void Attach()
    {
        if (attached) return;
        Application.Idle += OnIdle;
        attached = true;
    }

    public void Detach()
    {
        if (!attached) return;
        Application.Idle -= OnIdle;
        attached = false;
    }

    private void OnIdle(object sender, EventArgs e)
    {
        if (busy) return;
        EventHandler handler = FormNeedsTheme;
        if (handler == null) return;
        busy = true;
        try
        {
            FormCollection open = Application.OpenForms;
            if (open.Count == 0) return;
            Form[] forms = new Form[open.Count];
            ((ICollection)open).CopyTo(forms, 0);
            for (int i = 0; i < forms.Length; i++)
            {
                Form window = forms[i];
                if (window == null || window.IsDisposed || window.InvokeRequired) continue;
                if (registry.Contains(window)) continue;
                try { handler(window, EventArgs.Empty); }
                catch { }
            }
        }
        finally
        {
            busy = false;
        }
    }
}
