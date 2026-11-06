// Planet Crafter Mod Menu - a BepInEx 5 plugin that adds a MODS button to The Planet Crafter's main menu.
// It lists installed mods, turns them on or off, and installs or updates mods from a GitHub repo link
// (the newest release's .dll files plus model/texture files next to them). Written in C# 5 for the csc.exe that ships with Windows.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PlanetCrafterModMenu
{
    [BepInPlugin(GUID, "Planet Crafter Mod Menu", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "guy.planetcrafter.modmenu";
        public static BepInEx.Logging.ManualLogSource Log;

        string pluginsDir;
        string registryPath;
        Dictionary<string, RegEntry> registry = new Dictionary<string, RegEntry>(StringComparer.OrdinalIgnoreCase);
        List<ModEntry> mods = new List<ModEntry>();

        bool open;
        bool changesPending;
        bool busy;
        string repoInput = "";
        string status = "";
        Color statusColor = Color.white;
        Vector2 scroll;
        GameObject modsButton;
        EventSystem disabledEventSystem;
        bool prevCursorVisible;
        CursorLockMode prevLock;

        class RegEntry { public string Key; public string Name; public string Version; public string Repo; public bool Remove; }
        class ModEntry
        {
            public string Key;          // folder name (repo mods) or file name (loose dll)
            public string Name;
            public string Version;
            public string Repo;
            public List<string> Files = new List<string>(); // full paths as they are now
            public bool Enabled;
            public bool IsSelf;
            public bool Loaded;
        }

        // ---------------- lifecycle ----------------
        void Awake()
        {
            Log = Logger;
            pluginsDir = Paths.PluginPath;
            registryPath = Path.Combine(Paths.ConfigPath, "guy.planetcrafter.modmenu.registry.txt");
            LoadRegistry();
            CleanupPending();
            SceneManager.sceneLoaded += OnSceneLoaded;
            Logger.LogInfo("Planet Crafter Mod Menu loaded");
        }

        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            open = false;
            StartCoroutine(AddButtonSoon(s.name));
        }

        float nextButtonCheck;
        void Update()
        {
            // the first scene can load before Awake subscribes, so also poll now and then
            if (modsButton == null && Time.unscaledTime > nextButtonCheck)
            {
                nextButtonCheck = Time.unscaledTime + 1.5f;
                TryAddMenuButton(false);
            }
            if (open && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        IEnumerator AddButtonSoon(string scene)
        {
            yield return null;
            yield return null;
            Logger.LogInfo("Scene loaded: " + scene);
            TryAddMenuButton(true);
        }

        // ---------------- main menu button ----------------
        static string Label(Button b)
        {
            TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null) return t.text;
            Text lt = b.GetComponentInChildren<Text>(true);
            return lt != null ? lt.text : "";
        }

        void TryAddMenuButton(bool verbose)
        {
            if (modsButton != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<SpaceCraft.PlayerMainController>() != null) return; // in a world, not the main menu
            Button[] buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            if (buttons.Length == 0) return;
            Button template = null, quit = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                string l = Label(buttons[i]).Trim().ToLowerInvariant();
                if (verbose) Logger.LogInfo("  button '" + l + "' at " + HierPath(buttons[i].transform));
                if (l.Contains("quit") || l.Contains("exit")) quit = buttons[i];
                if (l.Contains("settings") || l.Contains("options")) template = buttons[i];
            }
            if (quit == null) return;
            if (template == null) template = quit;

            GameObject go = (GameObject)Instantiate(template.gameObject, template.transform.parent);
            go.name = "ModMenuButton";
            Button b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent(); // drops the original's inspector listeners
            b.onClick.AddListener(delegate
            {
                Open();
            });
            TMP_Text t = go.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = IsUpper(Label(template)) ? "MODS" : "Mods";
            else { Text lt = go.GetComponentInChildren<Text>(true); if (lt != null) lt.text = "Mods"; }

            // put it just above Quit
            RectTransform rt = go.GetComponent<RectTransform>();
            LayoutGroup lg = template.transform.parent.GetComponent<LayoutGroup>();
            if (lg != null)
            {
                go.transform.SetSiblingIndex(quit.transform.GetSiblingIndex());
            }
            else if (quit.transform.parent == template.transform.parent && template != quit)
            {
                RectTransform qt = quit.GetComponent<RectTransform>();
                RectTransform tt = template.GetComponent<RectTransform>();
                float step = qt.anchoredPosition.y - tt.anchoredPosition.y; // usually negative (quit is below)
                if (Mathf.Abs(step) < 1f) step = -tt.rect.height * 1.2f;
                rt.anchoredPosition = qt.anchoredPosition;
                qt.anchoredPosition = qt.anchoredPosition + new Vector2(0f, step);
            }
            else
            {
                RectTransform qt = quit.GetComponent<RectTransform>();
                rt.anchoredPosition = qt.anchoredPosition + new Vector2(0f, -qt.rect.height * 1.2f);
            }
            modsButton = go;
            Logger.LogInfo("Added MODS button to the main menu (template '" + Label(template) + "')");
        }

        static bool IsUpper(string s) { return s == s.ToUpperInvariant(); }

        static string HierPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        // ---------------- open / close ----------------
        void Open()
        {
            Scan();
            open = true;
            status = "";
            prevCursorVisible = Cursor.visible; prevLock = Cursor.lockState;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            if (EventSystem.current != null) { disabledEventSystem = EventSystem.current; disabledEventSystem.enabled = false; }
        }

        void Close()
        {
            open = false;
            Cursor.visible = prevCursorVisible; Cursor.lockState = prevLock;
            if (disabledEventSystem != null) { disabledEventSystem.enabled = true; disabledEventSystem = null; }
        }

        // ---------------- scanning ----------------
        void Scan()
        {
            mods.Clear();
            Dictionary<string, BepInEx.PluginInfo> byFile = new Dictionary<string, BepInEx.PluginInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, BepInEx.PluginInfo> kv in Chainloader.PluginInfos)
                if (!string.IsNullOrEmpty(kv.Value.Location)) byFile[System.IO.Path.GetFullPath(kv.Value.Location)] = kv.Value;

            // loose DLLs directly in plugins/
            foreach (string f in Directory.GetFiles(pluginsDir))
            {
                if (!IsModFile(f)) continue;
                ModEntry m = new ModEntry();
                m.Key = BaseName(System.IO.Path.GetFileName(f));
                m.Files.Add(f);
                Fill(m, byFile);
                mods.Add(m);
            }
            // one folder per mod
            foreach (string d in Directory.GetDirectories(pluginsDir))
            {
                List<string> files = new List<string>();
                foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) if (IsModFile(f)) files.Add(f);
                if (files.Count == 0) continue;
                ModEntry m = new ModEntry();
                m.Key = System.IO.Path.GetFileName(d);
                m.Files = files;
                Fill(m, byFile);
                mods.Add(m);
            }
            mods.Sort(delegate(ModEntry a, ModEntry b)
            {
                if (a.IsSelf != b.IsSelf) return a.IsSelf ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            SaveRegistry();
        }

        static bool IsModFile(string f)
        {
            return f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
        }

        static string BaseName(string file)
        {
            if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) file = file.Substring(0, file.Length - 9);
            return file;
        }

        void Fill(ModEntry m, Dictionary<string, BepInEx.PluginInfo> byFile)
        {
            m.Enabled = false;
            foreach (string f in m.Files) if (f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) m.Enabled = true;
            foreach (string f in m.Files)
            {
                BepInEx.PluginInfo pi;
                if (byFile.TryGetValue(System.IO.Path.GetFullPath(f), out pi))
                {
                    m.Loaded = true;
                    if (pi.Metadata.GUID == GUID) m.IsSelf = true;
                    if (m.Name == null) { m.Name = pi.Metadata.Name; m.Version = pi.Metadata.Version.ToString(); }
                }
            }
            RegEntry r;
            if (registry.TryGetValue(m.Key, out r))
            {
                if (m.Name == null) m.Name = r.Name;
                if (m.Version == null) m.Version = r.Version;
                m.Repo = r.Repo;
            }
            else { r = new RegEntry(); r.Key = m.Key; registry[m.Key] = r; }
            if (m.Name == null) m.Name = System.IO.Path.GetFileNameWithoutExtension(m.Key);
            if (m.Loaded) { r.Name = m.Name; r.Version = m.Version; }
        }

        // ---------------- toggle / remove ----------------
        void SetEnabled(ModEntry m, bool on)
        {
            List<string> now = new List<string>();
            foreach (string f in m.Files)
            {
                string target = on ? BaseName(f) : (f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? f : f + ".disabled");
                try
                {
                    if (target != f) File.Move(f, target);
                    now.Add(target);
                }
                catch (Exception e) { now.Add(f); SetStatus("Could not rename " + System.IO.Path.GetFileName(f) + ": " + e.Message, true); }
            }
            m.Files = now;
            m.Enabled = on;
            changesPending = true;
            Logger.LogInfo((on ? "Enabled " : "Disabled ") + m.Name);
        }

        void Remove(ModEntry m)
        {
            SetEnabled(m, false);
            RegEntry r;
            if (registry.TryGetValue(m.Key, out r)) r.Remove = true;
            SaveRegistry();
            CleanupPending(); // deletes right away when Windows lets us, otherwise on next start
            Scan();
            SetStatus("Removed " + m.Name + ".", false);
        }

        void CleanupPending()
        {
            try
            {
                foreach (string f in Directory.GetFiles(pluginsDir, "*.modmenu-old", SearchOption.AllDirectories))
                    try { File.Delete(f); } catch { }
                List<string> gone = new List<string>();
                foreach (RegEntry r in registry.Values)
                {
                    if (!r.Remove) continue;
                    string p = System.IO.Path.Combine(pluginsDir, r.Key);
                    try
                    {
                        if (Directory.Exists(p)) Directory.Delete(p, true);
                        if (File.Exists(p)) File.Delete(p);
                        if (File.Exists(p + ".disabled")) File.Delete(p + ".disabled");
                        gone.Add(r.Key);
                    }
                    catch { }
                }
                foreach (string k in gone) registry.Remove(k);
                if (gone.Count > 0) SaveRegistry();
            }
            catch (Exception e) { Logger.LogWarning("cleanup: " + e.Message); }
        }

        // ---------------- install from GitHub ----------------
        static readonly string[] AssetTypes = { ".dll", ".obj", ".mtl", ".png", ".jpg", ".wav", ".ogg", ".json", ".txt", ".bundle" };
        static bool IsAllowedAsset(string url)
        {
            foreach (string e in AssetTypes) if (url.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool ParseRepo(string input, out string owner, out string repo)
        {
            owner = repo = null;
            if (input == null) return false;
            string s = input.Trim();
            Match m = Regex.Match(s, @"github\.com[/:]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)");
            if (!m.Success) m = Regex.Match(s, @"^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)$");
            if (!m.Success) return false;
            owner = m.Groups[1].Value;
            repo = m.Groups[2].Value;
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(0, repo.Length - 4);
            return true;
        }

        IEnumerator Install(string input)
        {
            string owner, repo;
            if (!ParseRepo(input, out owner, out repo)) { SetStatus("That doesn't look like a GitHub repo link (github.com/owner/repo).", true); yield break; }
            busy = true;
            SetStatus("Looking up the latest release of " + owner + "/" + repo + "...", false);
            string api = "https://api.github.com/repos/" + owner + "/" + repo + "/releases/latest";
            UnityWebRequest req = UnityWebRequest.Get(api);
            req.SetRequestHeader("User-Agent", "PlanetCrafterModMenu");
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus(req.responseCode == 404 ? "No release found for " + owner + "/" + repo + ". The repo needs a GitHub release with a .dll attached."
                                                   : "GitHub request failed: " + req.error, true);
                busy = false; yield break;
            }
            string json = req.downloadHandler.text;
            Match tagM = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]*)\"");
            string tag = tagM.Success ? tagM.Groups[1].Value : "?";
            List<string> urls = new List<string>();
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                if (IsAllowedAsset(m.Groups[1].Value)) urls.Add(m.Groups[1].Value);
            bool hasDll = false;
            foreach (string u in urls) if (u.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) hasDll = true;
            if (!hasDll) { SetStatus("Release " + tag + " has no .dll files attached.", true); busy = false; yield break; }

            string key = repo;
            string dir = System.IO.Path.Combine(pluginsDir, key);
            Directory.CreateDirectory(dir);
            // if the mod is turned off, keep it off after updating
            bool keepDisabled = false;
            foreach (string f in Directory.GetFiles(dir, "*.dll.disabled", SearchOption.AllDirectories)) keepDisabled = true;

            foreach (string url in urls)
            {
                string file = Uri.UnescapeDataString(url.Substring(url.LastIndexOf('/') + 1));
                if (file.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) continue;
                SetStatus("Downloading " + file + " (" + tag + ")...", false);
                UnityWebRequest dl = UnityWebRequest.Get(url);
                dl.SetRequestHeader("User-Agent", "PlanetCrafterModMenu");
                yield return dl.SendWebRequest();
                if (dl.result != UnityWebRequest.Result.Success) { SetStatus("Download failed: " + dl.error, true); busy = false; yield break; }
                byte[] data = dl.downloadHandler.data;
                bool isDll = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                if (isDll && (data == null || data.Length < 2 || data[0] != (byte)'M' || data[1] != (byte)'Z')) { SetStatus(file + " is not a valid .dll.", true); busy = false; yield break; }
                string target = System.IO.Path.Combine(dir, keepDisabled && isDll ? file + ".disabled" : file);
                try
                {
                    // a loaded dll can't be overwritten on Windows but can be renamed; the old copy is deleted next start
                    foreach (string existing in new string[] { System.IO.Path.Combine(dir, file), System.IO.Path.Combine(dir, file + ".disabled") })
                        if (File.Exists(existing)) { string old = existing + "." + DateTime.Now.Ticks + ".modmenu-old"; File.Move(existing, old); }
                    File.WriteAllBytes(target, data);
                }
                catch (Exception e) { SetStatus("Could not save " + file + ": " + e.Message, true); busy = false; yield break; }
            }
            // if another copy of the same dll sits elsewhere in plugins/ (a hand install), turn it off so it doesn't load twice
            foreach (string url in urls)
            {
                string file = Uri.UnescapeDataString(url.Substring(url.LastIndexOf('/') + 1));
                if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string other in Directory.GetFiles(pluginsDir, file, SearchOption.AllDirectories))
                {
                    if (string.Equals(System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(other)), System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Move(other, other + ".disabled"); Logger.LogInfo("Turned off duplicate " + other); } catch { }
                }
            }

            RegEntry r;
            if (!registry.TryGetValue(key, out r)) { r = new RegEntry(); r.Key = key; registry[key] = r; }
            r.Repo = "https://github.com/" + owner + "/" + repo;
            r.Version = tag.TrimStart('v', 'V');
            r.Remove = false;
            if (r.Name == null) r.Name = repo;
            SaveRegistry();
            CleanupPending();
            Scan();
            changesPending = true;
            repoInput = "";
            SetStatus("Installed " + owner + "/" + repo + " " + tag + ". Restart the game to load it.", false);
            busy = false;
        }

        void SetStatus(string s, bool error)
        {
            status = s;
            statusColor = error ? new Color(1f, 0.45f, 0.4f) : new Color(0.6f, 1f, 0.6f);
            if (error) Logger.LogWarning(s); else Logger.LogInfo(s);
        }

        // ---------------- registry (plain text, one mod per line) ----------------
        void LoadRegistry()
        {
            registry.Clear();
            if (!File.Exists(registryPath)) return;
            foreach (string line in File.ReadAllLines(registryPath))
            {
                string[] p = line.Split('\t');
                if (p.Length < 5 || line.StartsWith("#")) continue;
                RegEntry r = new RegEntry();
                r.Key = p[0]; r.Name = Empty(p[1]); r.Version = Empty(p[2]); r.Repo = Empty(p[3]); r.Remove = p[4] == "remove";
                registry[r.Key] = r;
            }
        }

        static string Empty(string s) { return string.IsNullOrEmpty(s) ? null : s; }

        void SaveRegistry()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# key\tname\tversion\trepo\tstate  (written by Planet Crafter Mod Menu)");
                foreach (RegEntry r in registry.Values)
                    lines.Add(r.Key + "\t" + (r.Name ?? "") + "\t" + (r.Version ?? "") + "\t" + (r.Repo ?? "") + "\t" + (r.Remove ? "remove" : "keep"));
                File.WriteAllLines(registryPath, lines.ToArray());
            }
            catch (Exception e) { Logger.LogWarning("registry: " + e.Message); }
        }

        // ---------------- restart ----------------
        // The demo (Prologue) and the full game have different Steam app ids.
        static string SteamAppId()
        {
            return Application.dataPath.IndexOf("Demo", StringComparison.OrdinalIgnoreCase) >= 0 ? "1754850" : "1284190";
        }

        void Restart()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    "/c ping -n 4 127.0.0.1 >nul & start \"\" steam://rungameid/" + SteamAppId());
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e) { Logger.LogWarning("restart: " + e.Message); }
            Application.Quit();
        }

        // ---------------- UI ----------------
        GUIStyle title, rowName, rowSub, btn, field, panel, onBtn, offBtn;
        Texture2D bg, rowBg, black;

        void InitStyles()
        {
            if (title != null) return;
            bg = Tex(new Color(0.09f, 0.07f, 0.06f, 0.96f));
            rowBg = Tex(new Color(1f, 1f, 1f, 0.06f));
            black = Tex(new Color(0f, 0f, 0f, 0.6f));
            panel = new GUIStyle(GUI.skin.box); panel.normal.background = bg; panel.border = new RectOffset(0, 0, 0, 0);
            title = new GUIStyle(GUI.skin.label); title.fontSize = 34; title.fontStyle = FontStyle.Bold; title.normal.textColor = new Color(1f, 0.6f, 0.2f);
            rowName = new GUIStyle(GUI.skin.label); rowName.fontSize = 20; rowName.fontStyle = FontStyle.Bold; rowName.normal.textColor = Color.white;
            rowSub = new GUIStyle(GUI.skin.label); rowSub.fontSize = 13; rowSub.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            btn = new GUIStyle(GUI.skin.button); btn.fontSize = 16; btn.fontStyle = FontStyle.Bold;
            onBtn = new GUIStyle(btn); onBtn.normal.textColor = onBtn.hover.textColor = new Color(0.5f, 1f, 0.4f);
            offBtn = new GUIStyle(btn); offBtn.normal.textColor = offBtn.hover.textColor = new Color(1f, 0.5f, 0.45f);
            field = new GUIStyle(GUI.skin.textField); field.fontSize = 17; field.alignment = TextAnchor.MiddleLeft; field.padding = new RectOffset(8, 8, 4, 4);
        }

        static Texture2D Tex(Color c) { Texture2D t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        void OnGUI()
        {
            if (!open) return;
            InitStyles();
            GUI.depth = -1000;
            float W = Screen.width, H = Screen.height;
            GUI.DrawTexture(new Rect(0, 0, W, H), black);
            float pw = Mathf.Min(820f, W - 40f), ph = Mathf.Min(640f, H - 40f);
            Rect p = new Rect((W - pw) / 2f, (H - ph) / 2f, pw, ph);
            GUI.Box(p, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(p.x + 24, p.y + 16, p.width - 48, p.height - 32));

            GUILayout.BeginHorizontal();
            GUILayout.Label("MODS", title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close  [Esc]", btn, GUILayout.Width(140), GUILayout.Height(36))) Close();
            GUILayout.EndHorizontal();
            GUILayout.Label(mods.Count + " installed  ·  changes apply after a restart", rowSub);
            GUILayout.Space(6);

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            ModEntry toRemove = null;
            foreach (ModEntry m in mods)
            {
                Rect row = EditorRow();
                GUI.DrawTexture(row, rowBg);
                GUILayout.BeginHorizontal(GUILayout.Height(58));
                GUILayout.Space(10);
                GUILayout.BeginVertical();
                GUILayout.Space(6);
                GUILayout.Label(m.Name + (m.Version != null ? "  v" + m.Version : ""), rowName);
                string sub = m.IsSelf ? "this menu (always on)" : (m.Repo != null ? m.Repo : "installed by hand: " + m.Key);
                if (m.Enabled != m.Loaded && !m.IsSelf) sub += m.Enabled ? "  ·  will load after restart" : "  ·  will unload after restart";
                GUILayout.Label(sub, rowSub);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUI.enabled = !busy && !m.IsSelf;
                if (m.Repo != null && GUILayout.Button("Update", btn, GUILayout.Width(90), GUILayout.Height(36)))
                { repoInput = m.Repo; StartCoroutine(Install(m.Repo)); }
                if (m.Repo != null && GUILayout.Button("Remove", btn, GUILayout.Width(90), GUILayout.Height(36))) toRemove = m;
                if (GUILayout.Button(m.Enabled ? "ON" : "OFF", m.Enabled ? onBtn : offBtn, GUILayout.Width(80), GUILayout.Height(36)))
                    SetEnabled(m, !m.Enabled);
                GUI.enabled = true;
                GUILayout.Space(10);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            if (toRemove != null) Remove(toRemove);

            GUILayout.Space(8);
            GUILayout.Label("Install a mod from a GitHub repo link (downloads the files of its latest release):", rowSub);
            GUILayout.BeginHorizontal();
            GUI.enabled = !busy;
            repoInput = GUILayout.TextField(repoInput, field, GUILayout.Height(36), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(busy ? "..." : "Install", btn, GUILayout.Width(110), GUILayout.Height(36))) StartCoroutine(Install(repoInput));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Mods run code on your PC. Only install from repos you trust.", rowSub);
            if (status.Length > 0)
            {
                GUIStyle s = new GUIStyle(rowSub); s.fontSize = 15; s.normal.textColor = statusColor; s.wordWrap = true;
                GUILayout.Label(status, s);
            }
            if (changesPending)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUIStyle s = new GUIStyle(rowName); s.fontSize = 16; s.normal.textColor = new Color(1f, 0.8f, 0.3f);
                GUILayout.Label("Restart the game to apply your changes.", s);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Restart now", btn, GUILayout.Width(150), GUILayout.Height(34))) Restart();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        // background rect for the row that is about to be laid out
        Rect EditorRow()
        {
            Rect r = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
            return new Rect(r.x, r.y, r.width, 58);
        }
    }
}
