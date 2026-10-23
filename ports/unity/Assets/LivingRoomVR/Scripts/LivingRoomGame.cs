// Living Room VR - Unity version
// Loads the 120 Blender-baked living rooms (glTF + baked lightmap) at runtime from StreamingAssets,
// gives you a first-person body and a list of chores to do in each room.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;

namespace LivingRoomVR
{
    [Serializable] public class DoorInfo { public string wall; public float t, w, h; }
    [Serializable] public class RoomInfo { public string id, name, style; public float W, D, H, lm_scale; public DoorInfo door; public bool fireplace; }
    [Serializable] class RoomList { public RoomInfo[] rooms; }

    public class ChoreTask
    {
        public string type, label;
        public List<Interactable> items = new List<Interactable>();
        public int Left => items.Count(i => i && i.bad);
        public bool Done => Left == 0;
    }

    public class LivingRoomGame : MonoBehaviour
    {
        [Header("Player")]
        public float walkSpeed = 2.2f;
        public float lookSpeed = 2.0f;
        public float reach = 3.2f;

        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "pillow", "Put the pillows back on the sofa" },
            { "book", "Put the books back" },
            { "frame", "Straighten the picture frames" },
            { "tv", "Turn off the TV" },
            { "lamp", "Turn off the lamps" },
            { "plant", "Water the plants" },
            { "trash", "Pick up the trash" },
        };

        RoomInfo[] m_Rooms = new RoomInfo[0];
        int m_Index;
        GltfImport m_Gltf;
        GameObject m_Root;
        Texture2D m_Lightmap;
        readonly List<Material> m_OwnedMaterials = new List<Material>();
        readonly List<ChoreTask> m_Tasks = new List<ChoreTask>();
        readonly List<Interactable> m_Items = new List<Interactable>();
        RoomInfo m_Room;
        bool m_Loading = true;
        string m_Status = "Loading…";
        int m_Coins;
        float m_DoneTimer = -1;
        bool m_Menu;
        Vector2 m_MenuScroll;
        string m_Toast; float m_ToastT;

        // player
        CharacterController m_Body;
        Camera m_Cam;
        float m_Yaw, m_Pitch, m_VelY;
        Interactable m_Target;

        // touch
        int m_MoveFinger = -1, m_LookFinger = -1;
        Vector2 m_MoveStart, m_MoveCur, m_LookLast, m_LookStart; float m_LookStartT;

        static Shader s_Baked, s_BakedT, s_Unlit, s_Lit;

        void Awake()
        {
            Application.targetFrameRate = 60;
            s_Baked = Shader.Find("LRVR/Baked");
            s_BakedT = Shader.Find("LRVR/BakedTransparent");
            s_Unlit = Shader.Find("LRVR/Unlit");
            s_Lit = Shader.Find("LRVR/Lit");
            Shader.SetGlobalColor("_LRAmbient", new Color(1.35f, 1.25f, 1.1f));

            var player = new GameObject("Player");
            m_Body = player.AddComponent<CharacterController>();
            m_Body.height = 1.7f; m_Body.radius = 0.22f; m_Body.center = new Vector3(0, 0.85f, 0);
            m_Body.stepOffset = 0.25f; m_Body.skinWidth = 0.02f;
            m_Cam = Camera.main;
            if (!m_Cam) { var c = new GameObject("Main Camera"); c.tag = "MainCamera"; m_Cam = c.AddComponent<Camera>(); c.AddComponent<AudioListener>(); }
            m_Cam.transform.SetParent(player.transform, false);
            m_Cam.transform.localPosition = new Vector3(0, 1.62f, 0);
            m_Cam.nearClipPlane = 0.05f; m_Cam.farClipPlane = 60f; m_Cam.fieldOfView = 70f;
            m_Cam.clearFlags = CameraClearFlags.SolidColor; m_Cam.backgroundColor = new Color(0.05f, 0.045f, 0.04f);
            m_Coins = PlayerPrefs.GetInt("lrvr_coins", 0);
        }

        IEnumerator Start()
        {
            string txt = null;
            yield return Fetch("rooms.json", r => txt = r.downloadHandler.text);
            if (string.IsNullOrEmpty(txt)) { m_Status = "Could not read StreamingAssets/rooms.json"; yield break; }
            m_Rooms = JsonUtility.FromJson<RoomList>("{\"rooms\":" + txt + "}").rooms;
            int start = PlayerPrefs.GetInt("lrvr_room", 0);
            string shot = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-lrvrRoom") int.TryParse(args[i + 1], out start);
                if (args[i] == "-lrvrShot") shot = args[i + 1];
            }
            _ = LoadRoom(Mathf.Clamp(start, 0, m_Rooms.Length - 1));
            if (shot != null) StartCoroutine(AutoShot(shot));
        }

        // test helper: -lrvrShot <file.png> saves a screenshot after the room loads, then quits
        IEnumerator AutoShot(string path)
        {
            while (m_Loading) yield return null;
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(0.5f);
            m_Pitch = 8; m_Yaw += 140;
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(path.Replace(".png", "_b.png"));
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        static string Url(string rel)
        {
            string p = Path.Combine(Application.streamingAssetsPath, rel).Replace('\\', '/');
            return p.Contains("://") ? p : "file://" + p;
        }

        static IEnumerator Fetch(string rel, Action<UnityWebRequest> ok, bool texture = false)
        {
            using (var r = texture ? UnityWebRequestTexture.GetTexture(Url(rel), true) : UnityWebRequest.Get(Url(rel)))
            {
                yield return r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.Success) ok(r);
                else Debug.LogWarning($"[LRVR] {rel}: {r.error}");
            }
        }

        static async Task<UnityWebRequest> FetchAsync(UnityWebRequest r)
        {
            var op = r.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            return r;
        }

        // ------------------------------------------------------------ room loading
        public async Task LoadRoom(int index)
        {
            if (m_Rooms.Length == 0) return;
            index = (index % m_Rooms.Length + m_Rooms.Length) % m_Rooms.Length;
            m_Loading = true; m_Menu = false; m_DoneTimer = -1;
            m_Index = index; m_Room = m_Rooms[index];
            m_Status = $"Loading {m_Room.name}…";
            PlayerPrefs.SetInt("lrvr_room", index);
            Unload();

            byte[] bytes;
            using (var r = await FetchAsync(UnityWebRequest.Get(Url($"rooms/{m_Room.id}.glb"))))
            {
                if (r.result != UnityWebRequest.Result.Success) { m_Status = $"Missing rooms/{m_Room.id}.glb in StreamingAssets"; return; }
                bytes = r.downloadHandler.data;
            }
            using (var r = await FetchAsync(UnityWebRequestTexture.GetTexture(Url($"rooms/{m_Room.id}_lightmap.jpg"), true)))
                if (r.result == UnityWebRequest.Result.Success) m_Lightmap = DownloadHandlerTexture.GetContent(r);

            m_Gltf = new GltfImport(null, null, new LRMaterialGenerator());
            bool ok = await m_Gltf.LoadGltfBinary(bytes, new Uri(Url($"rooms/{m_Room.id}.glb")));
            if (!ok) { m_Status = "Failed to read " + m_Room.id; return; }
            m_Root = new GameObject(m_Room.id);
            await m_Gltf.InstantiateMainSceneAsync(m_Root.transform);

            var nodes = ReadNodeExtras(bytes);
            PrepareRoom(nodes);
            SetupTasks();
            m_Loading = false;
            Toast(m_Room.name);
            Debug.Log($"[LRVR] {m_Room.id}: items={m_Items.Count} tasks={string.Join(",", m_Tasks.Select(t => t.type + ":" + t.items.Count))} sx={m_SX} sz={m_SZ} lightmap={(m_Lightmap ? m_Lightmap.width : 0)} player={m_Body.transform.position}");
        }

        void Unload()
        {
            foreach (var m in m_OwnedMaterials) if (m) Destroy(m);
            m_OwnedMaterials.Clear();
            m_Tasks.Clear(); m_Items.Clear(); m_Target = null;
            if (m_Root) Destroy(m_Root);
            m_Gltf?.Dispose(); m_Gltf = null;
            if (m_Lightmap) Destroy(m_Lightmap);
        }

        /// node name -> extras dictionary (glTFast does not import extras, so read them from the GLB JSON chunk)
        static Dictionary<string, Dictionary<string, object>> ReadNodeExtras(byte[] glb)
        {
            var res = new Dictionary<string, Dictionary<string, object>>();
            int len = BitConverter.ToInt32(glb, 12);
            string json = System.Text.Encoding.UTF8.GetString(glb, 20, len);
            var root = MiniJson.Obj(MiniJson.Parse(json));
            if (MiniJson.Get(root, "nodes") is List<object> nodes)
                foreach (var o in nodes)
                {
                    var n = MiniJson.Obj(o);
                    if (MiniJson.Get(n, "name") is string name && MiniJson.Obj(MiniJson.Get(n, "extras")) is Dictionary<string, object> ex)
                        res[name] = ex;
                }
            return res;
        }

        // blender (x, y, z-up) -> room-local unity coordinates
        float m_SX = -1, m_SZ = -1;
        Vector3 B2U(double x, double y, double z) => new Vector3(m_SX * (float)x, (float)z, m_SZ * (float)y);

        void PrepareRoom(Dictionary<string, Dictionary<string, object>> extras)
        {
            var all = m_Root.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);

            // work out glTF -> Unity handedness from the door marker (robust to importer conventions)
            var door = Find("DOOR");
            Vector3 doorPos = door ? m_Root.transform.InverseTransformPoint(door.position) : new Vector3(0, 0, m_Room.D / 2);
            var dw = m_Room.door?.wall ?? "S"; float t = m_Room.door?.t ?? 0;
            if (dw == "S" || dw == "N")
            {
                float by = dw == "S" ? -m_Room.D / 2 : m_Room.D / 2;
                if (Mathf.Abs(doorPos.z) > 0.3f) m_SZ = Mathf.Sign(doorPos.z / by);
                if (Mathf.Abs(t) > 0.15f && Mathf.Abs(doorPos.x) > 0.1f) m_SX = Mathf.Sign(doorPos.x / t);
            }
            else
            {
                float bx = dw == "E" ? m_Room.W / 2 : -m_Room.W / 2;
                if (Mathf.Abs(doorPos.x) > 0.3f) m_SX = Mathf.Sign(doorPos.x / bx);
                if (Mathf.Abs(t) > 0.15f && Mathf.Abs(doorPos.z) > 0.1f) m_SZ = Mathf.Sign(doorPos.z / t);
            }

            // baked shell -> lightmapped shader
            var cache = new Dictionary<Material, Material>();
            float lmScale = (m_Room.lm_scale > 0 ? m_Room.lm_scale : 1f) * 0.62f;
            var st = Find("STATIC");
            if (st)
                foreach (var r in st.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i]; if (!src) continue;
                        if (!cache.TryGetValue(src, out var dst))
                        {
                            dst = new Material(src) { shader = src.renderQueue >= 3000 ? s_BakedT : s_Baked, renderQueue = src.renderQueue };
                            dst.SetTexture("_LightMap", m_Lightmap);
                            bool emissive = src.GetColor("_EmissionColor").maxColorComponent > 0.01f;
                            dst.SetFloat("_LMScale", emissive ? 0 : lmScale);
                            if (emissive && dst.GetColor("_Color").maxColorComponent > 0) dst.SetColor("_Color", Color.black);
                            m_OwnedMaterials.Add(dst); cache[src] = dst;
                        }
                        mats[i] = dst;
                    }
                    r.sharedMaterials = mats;
                }

            // outside backdrops: unlit picture, trimmed to the wall
            foreach (var o in all.Where(x => x.name.StartsWith("OUTSIDE")))
            {
                var parts = o.name.Split('_'); string w = parts.Length > 1 ? parts[1] : "N";
                float L = (w == "N" || w == "S") ? m_Room.W : m_Room.D;
                var s = o.localScale; s.x *= (L + 0.3f) / (L + 6f); o.localScale = s;
                foreach (var r in o.GetComponentsInChildren<Renderer>())
                {
                    var src = r.sharedMaterial; if (!src) continue;
                    var m = new Material(src) { shader = s_Unlit, renderQueue = 2000 };
                    if (src.GetTexture("_MainTex") == null && src.GetTexture("_EmissionMap") != null) m.SetTexture("_MainTex", src.GetTexture("_EmissionMap"));
                    m.SetColor("_Color", Color.white * 1.05f);
                    m_OwnedMaterials.Add(m); r.sharedMaterial = m;
                }
            }

            // floor + furniture colliders (footprints exported from Blender)
            var floor = new GameObject("Floor").AddComponent<BoxCollider>();
            floor.transform.SetParent(m_Root.transform, false);
            floor.center = new Vector3(0, -0.05f, 0); floor.size = new Vector3(m_Room.W + 4, 0.1f, m_Room.D + 4);
            foreach (var kv in extras)
            {
                var ex = kv.Value;
                if (MiniJson.Num(MiniJson.Get(ex, "collide")) < 0.5) continue;
                var kind = MiniJson.Get(ex, "kind") as string;
                if (kind == "rug" || kind == "wall") continue;
                if (!(MiniJson.Get(ex, "bb") is List<object> bb) || bb.Count < 6) continue;
                Vector3 a = B2U(MiniJson.Num(bb[0]), MiniJson.Num(bb[1]), MiniJson.Num(bb[2]));
                Vector3 b = B2U(MiniJson.Num(bb[3]), MiniJson.Num(bb[4]), MiniJson.Num(bb[5]));
                var bc = new GameObject("COL_" + kv.Key).AddComponent<BoxCollider>();
                bc.gameObject.layer = 2; // Ignore Raycast: never blocks clicking on chores
                bc.transform.SetParent(m_Root.transform, false);
                bc.center = (a + b) / 2;
                var size = new Vector3(Mathf.Abs(a.x - b.x) - 0.08f, Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z) - 0.08f);
                bc.size = Vector3.Max(size, Vector3.one * 0.05f);
            }

            // interactables
            foreach (var o in all.Where(x => x.name.StartsWith("INT_")))
            {
                extras.TryGetValue(o.name, out var ex);
                string type = MiniJson.Get(ex, "task") as string ?? o.name.Split('_')[1];
                if (!Labels.ContainsKey(type)) continue;
                var it = o.gameObject.AddComponent<Interactable>();
                it.type = type;
                it.homeParent = o.parent; it.homeLocalPos = o.localPosition; it.homeLocalRot = o.localRotation;
                it.renderers = o.GetComponentsInChildren<Renderer>();
                AddPickCollider(it);
                m_Items.Add(it);
            }
            // lamps & tv start off
            foreach (var it in m_Items) { if (it.type == "lamp") SetLamp(it, false); if (it.type == "tv") SetTV(it, false); }

            // spawn just inside the door, facing the room
            Vector3 inward = new Vector3(-doorPos.x, 0, -doorPos.z).normalized;
            if (inward == Vector3.zero) inward = Vector3.forward;
            Vector3 spawn = new Vector3(doorPos.x, 0.02f, doorPos.z) + inward * 0.9f;
            // closed door leaf in the doorway (single-room mode has no hallway behind it)
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "DoorLeaf";
            Destroy(leaf.GetComponent<Collider>());
            leaf.transform.SetParent(m_Root.transform, false);
            float dwid = m_Room.door != null && m_Room.door.w > 0 ? m_Room.door.w : 1f;
            float dh = m_Room.door != null && m_Room.door.h > 0 ? m_Room.door.h : 2.1f;
            bool ns = dw == "S" || dw == "N";
            leaf.transform.localScale = ns ? new Vector3(dwid, dh, 0.045f) : new Vector3(0.045f, dh, dwid);
            leaf.transform.localPosition = new Vector3(doorPos.x, dh / 2f, doorPos.z) - inward * 0.05f;
            var leafMat = new Material(s_Lit);
            leafMat.SetColor("_Color", new Color(0.36f, 0.24f, 0.15f));
            m_OwnedMaterials.Add(leafMat);
            leaf.GetComponent<Renderer>().sharedMaterial = leafMat;

            m_Body.enabled = false;
            m_Body.transform.position = ClampToRoom(spawn);
            m_Body.enabled = true;
            m_Yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg; m_Pitch = 0;
        }

        void AddPickCollider(Interactable it)
        {
            var t = it.transform;
            bool any = false; var b = new Bounds();
            foreach (var r in it.renderers)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = t.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (!any) return;
            var bc = it.gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.center = b.center; bc.size = Vector3.Max(b.size, Vector3.one * 0.06f);
        }

        Vector3 ClampToRoom(Vector3 p)
        {
            float hx = m_Room.W / 2 - 0.3f, hz = m_Room.D / 2 - 0.3f;
            p.x = Mathf.Clamp(p.x, -hx, hx); p.z = Mathf.Clamp(p.z, -hz, hz);
            return p;
        }

        // ------------------------------------------------------------ chores
        void SetupTasks()
        {
            var byType = m_Items.GroupBy(i => i.type).ToDictionary(g => g.Key, g => g.ToList());
            var options = new List<string>();
            foreach (var k in new[] { "book", "frame", "tv", "lamp", "plant" }) if (byType.ContainsKey(k)) options.Add(k);
            Shuffle(options);
            var pick = new List<string> { "trash" };
            if (byType.ContainsKey("pillow")) pick.Add("pillow");
            int want = m_Room.W * m_Room.D > 34 ? 6 : 5;
            foreach (var o in options) { if (pick.Count >= want) break; pick.Add(o); }

            foreach (var type in pick)
            {
                var task = new ChoreTask { type = type, label = Labels[type] };
                switch (type)
                {
                    case "pillow":
                    case "book":
                        foreach (var it in Shuffled(byType[type]).Take(3)) { Scatter(it); task.items.Add(it); }
                        break;
                    case "frame":
                        foreach (var it in byType[type].Take(3)) { Tilt(it); task.items.Add(it); }
                        break;
                    case "lamp":
                        foreach (var it in byType[type]) { SetLamp(it, true); task.items.Add(it); }
                        break;
                    case "tv":
                        foreach (var it in byType[type]) { SetTV(it, true); task.items.Add(it); }
                        break;
                    case "plant":
                        foreach (var it in Shuffled(byType[type]).Take(3)) task.items.Add(it);
                        break;
                    case "trash":
                        for (int i = 0; i < UnityEngine.Random.Range(4, 7); i++) task.items.Add(SpawnTrash(i));
                        break;
                }
                foreach (var it in task.items) { it.bad = true; it.isTask = true; }
                if (task.items.Count > 0) m_Tasks.Add(task);
            }
        }

        Vector3 RandomFloorPoint(Vector3 near, float maxD = 2.2f)
        {
            for (int i = 0; i < 80; i++)
            {
                Vector3 p;
                if (i < 50) { var a = UnityEngine.Random.value * Mathf.PI * 2; var d = UnityEngine.Random.Range(0.5f, maxD); p = new Vector3(near.x + Mathf.Cos(a) * d, 0, near.z + Mathf.Sin(a) * d); }
                else p = new Vector3(UnityEngine.Random.Range(-m_Room.W / 2 + 0.5f, m_Room.W / 2 - 0.5f), 0, UnityEngine.Random.Range(-m_Room.D / 2 + 0.5f, m_Room.D / 2 - 0.5f));
                if (Mathf.Abs(p.x) > m_Room.W / 2 - 0.45f || Mathf.Abs(p.z) > m_Room.D / 2 - 0.45f) continue;
                if (Physics.CheckSphere(p + Vector3.up * 0.3f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
                return p;
            }
            return Vector3.zero;
        }

        void Scatter(Interactable it)
        {
            var p = RandomFloorPoint(it.transform.position);
            it.transform.SetParent(m_Root.transform, true);
            it.transform.position = p;
            it.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(-15f, 15f), UnityEngine.Random.Range(0, 360f), it.type == "book" ? 90 : UnityEngine.Random.Range(-80f, -100f));
            Settle(it);
        }

        void Settle(Interactable it)
        {
            float minY = float.MaxValue;
            foreach (var r in it.renderers) minY = Mathf.Min(minY, r.bounds.min.y);
            if (minY < float.MaxValue) it.transform.position += Vector3.up * (0.004f - minY);
        }

        void Tilt(Interactable it)
        {
            var b = WorldBounds(it);
            Vector3 axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward; // thin axis = wall normal
            float a = (UnityEngine.Random.value < 0.5f ? -1 : 1) * UnityEngine.Random.Range(10f, 18f);
            it.transform.RotateAround(b.center, axis, a);
        }

        static Bounds WorldBounds(Interactable it)
        {
            var b = new Bounds(it.transform.position, Vector3.zero); bool first = true;
            foreach (var r in it.renderers) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            return b;
        }

        void SetLamp(Interactable it, bool on)
        {
            foreach (var r in it.renderers)
                foreach (var m in r.materials)
                {
                    var n = m.name.ToLowerInvariant();
                    if (n.Contains("bulb") || n.Contains("shade")) m.SetColor("_EmissionColor", on ? new Color(1f, 0.72f, 0.42f) * (n.Contains("bulb") ? 3f : 1.2f) : Color.black);
                }
        }

        void SetTV(Interactable it, bool on)
        {
            foreach (var r in it.renderers)
                foreach (var m in r.materials)
                    if (m.name.ToLowerInvariant().Contains("screen"))
                        m.SetColor("_EmissionColor", on ? new Color(0.35f, 0.55f, 0.95f) * 1.4f : Color.black);
        }

        Interactable SpawnTrash(int i)
        {
            bool can = i % 3 == 2;
            var go = GameObject.CreatePrimitive(can ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
            go.name = can ? "TRASH_can" : "TRASH_paper";
            go.transform.SetParent(m_Root.transform, false);
            go.transform.localScale = can ? new Vector3(0.066f, 0.06f, 0.066f) : Vector3.one * UnityEngine.Random.Range(0.07f, 0.1f);
            go.transform.position = RandomFloorPoint(m_Body.transform.position, 3f) + Vector3.up * (can ? 0.033f : 0.04f);
            if (can) go.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 90);
            var mat = new Material(s_Lit);
            mat.SetColor("_Color", can ? new Color(0.75f, 0.08f, 0.06f) : new Color(0.88f, 0.86f, 0.8f));
            m_OwnedMaterials.Add(mat);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Collider>().isTrigger = true;
            var it = go.AddComponent<Interactable>();
            it.type = "trash"; it.renderers = new[] { go.GetComponent<Renderer>() };
            return it;
        }

        void Fix(Interactable it)
        {
            if (!it || !it.bad) return;
            it.bad = false;
            it.SetHighlight(0);
            switch (it.type)
            {
                case "pillow":
                case "book":
                case "frame":
                    StartCoroutine(FlyHome(it)); break;
                case "lamp": SetLamp(it, false); break;
                case "tv": SetTV(it, false); break;
                case "plant": StartCoroutine(Bounce(it.transform)); break;
                case "trash": StartCoroutine(Vanish(it.gameObject)); break;
            }
            m_Coins += 10;
            var task = m_Tasks.FirstOrDefault(t => t.items.Contains(it));
            if (task != null && task.Done) { m_Coins += 25; Toast("✔ " + task.label); }
            if (m_Tasks.Count > 0 && m_Tasks.All(t => t.Done))
            {
                m_Coins += 50; Toast("Room clean! +50");
                PlayerPrefs.SetInt("lrvr_done_" + m_Room.id, 1);
                m_DoneTimer = 4f;
            }
            PlayerPrefs.SetInt("lrvr_coins", m_Coins);
        }

        IEnumerator FlyHome(Interactable it)
        {
            var t = it.transform;
            t.SetParent(it.homeParent, true);
            Vector3 p0 = t.localPosition; Quaternion r0 = t.localRotation;
            for (float k = 0; k < 1; k += Time.deltaTime / 0.45f)
            {
                float e = k * k * (3 - 2 * k);
                t.localPosition = Vector3.Lerp(p0, it.homeLocalPos, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.15f;
                t.localRotation = Quaternion.Slerp(r0, it.homeLocalRot, e);
                yield return null;
            }
            t.localPosition = it.homeLocalPos; t.localRotation = it.homeLocalRot;
        }

        static IEnumerator Bounce(Transform t)
        {
            Vector3 s0 = t.localScale;
            for (float k = 0; k < 1; k += Time.deltaTime / 0.6f) { t.localScale = s0 * (1 + Mathf.Sin(k * Mathf.PI * 3) * 0.04f * (1 - k)); yield return null; }
            t.localScale = s0;
        }

        static IEnumerator Vanish(GameObject g)
        {
            Vector3 s0 = g.transform.localScale;
            for (float k = 0; k < 1 && g; k += Time.deltaTime / 0.3f) { g.transform.localScale = s0 * (1 - k); yield return null; }
            if (g) g.SetActive(false);
        }

        // ------------------------------------------------------------ input & movement
        static readonly Dictionary<string, bool> s_Axes = new Dictionary<string, bool>();
        static float Axis(string n)
        {
            if (s_Axes.TryGetValue(n, out var ok) && !ok) return 0;
            try { var v = Input.GetAxis(n); s_Axes[n] = true; return v; }
            catch { s_Axes[n] = false; return 0; }
        }

        void Update()
        {
            if (m_Toast != null && (m_ToastT -= Time.deltaTime) < 0) m_Toast = null;
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton7)) ToggleMenu();
            if (Input.GetKeyDown(KeyCode.Escape)) { if (m_Menu) ToggleMenu(); else Cursor.lockState = CursorLockMode.None; }
            if (m_Loading || m_Menu) return;
            if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.JoystickButton5) || Input.GetKeyDown(KeyCode.PageDown)) { _ = LoadRoom(m_Index + 1); return; }
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.JoystickButton4) || Input.GetKeyDown(KeyCode.PageUp)) { _ = LoadRoom(m_Index - 1); return; }
            if (m_DoneTimer > 0 && (m_DoneTimer -= Time.deltaTime) <= 0) { _ = LoadRoom(m_Index + 1); return; }

            Vector2 move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            Vector2 look = new Vector2(Axis("LookX"), -Axis("LookY")) * 120f * Time.deltaTime;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked) look += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * lookSpeed;
            else if (Input.GetMouseButton(1)) look += new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * lookSpeed;
            if (Input.GetKey(KeyCode.Q)) look.x -= 90 * Time.deltaTime;

            bool tapInteract = false; Vector2 tapPos = default;
            HandleTouch(ref move, ref look, ref tapInteract, ref tapPos);

            m_Yaw += look.x; m_Pitch = Mathf.Clamp(m_Pitch - look.y, -80, 80);
            m_Body.transform.rotation = Quaternion.Euler(0, m_Yaw, 0);
            m_Cam.transform.localRotation = Quaternion.Euler(m_Pitch, 0, 0);

            float speed = walkSpeed * (Input.GetKey(KeyCode.LeftShift) ? 1.6f : 1f);
            Vector3 dir = m_Body.transform.TransformDirection(new Vector3(move.x, 0, move.y));
            if (dir.sqrMagnitude > 1) dir.Normalize();
            m_VelY = m_Body.isGrounded ? -1f : m_VelY - 9.8f * Time.deltaTime;
            m_Body.Move((dir * speed + Vector3.up * m_VelY) * Time.deltaTime);
            var cp = ClampToRoom(m_Body.transform.position);
            if (cp.y < -2) cp.y = 0.05f;
            if ((cp - m_Body.transform.position).sqrMagnitude > 1e-6f) { m_Body.enabled = false; m_Body.transform.position = cp; m_Body.enabled = true; }

            // aim: screen centre when the mouse is captured (or with a gamepad), otherwise the pointer
            Vector2 aim = locked || Input.touchCount > 0 || !Input.mousePresent ? new Vector2(Screen.width / 2f, Screen.height / 2f) : (Vector2)Input.mousePosition;
            if (tapInteract) aim = tapPos;
            var target = Pick(aim);
            if (target != m_Target) { if (m_Target) m_Target.SetHighlight(0); m_Target = target; if (m_Target) m_Target.SetHighlight(0.12f); }

            bool click = Input.GetMouseButtonDown(0) && GUIUtility.hotControl == 0;
            if (click && !locked && Input.touchCount == 0 && target == null && Input.mousePresent) Cursor.lockState = CursorLockMode.Locked;
            if (target && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0) || click || tapInteract)) Fix(target);
        }

        Interactable Pick(Vector2 screen)
        {
            var ray = m_Cam.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, reach, ~(1 << 2), QueryTriggerInteraction.Collide);
            Interactable best = null; float bd = float.MaxValue;
            foreach (var h in hits)
            {
                var it = h.collider.GetComponentInParent<Interactable>();
                if (it && it.bad && h.distance < bd) { best = it; bd = h.distance; }
            }
            return best;
        }

        void HandleTouch(ref Vector2 move, ref Vector2 look, ref bool tap, ref Vector2 tapPos)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var tch = Input.GetTouch(i);
                if (tch.phase == TouchPhase.Began)
                {
                    if (tch.position.x < Screen.width * 0.4f && m_MoveFinger < 0) { m_MoveFinger = tch.fingerId; m_MoveStart = m_MoveCur = tch.position; }
                    else if (m_LookFinger < 0) { m_LookFinger = tch.fingerId; m_LookLast = m_LookStart = tch.position; m_LookStartT = Time.time; }
                }
                if (tch.fingerId == m_MoveFinger)
                {
                    m_MoveCur = tch.position;
                    var d = (m_MoveCur - m_MoveStart) / (Screen.height * 0.12f);
                    move += Vector2.ClampMagnitude(d, 1);
                    if (tch.phase == TouchPhase.Ended || tch.phase == TouchPhase.Canceled) m_MoveFinger = -1;
                }
                if (tch.fingerId == m_LookFinger)
                {
                    look += (tch.position - m_LookLast) * (180f / Screen.height);
                    m_LookLast = tch.position;
                    if (tch.phase == TouchPhase.Ended || tch.phase == TouchPhase.Canceled)
                    {
                        if (Time.time - m_LookStartT < 0.3f && (tch.position - m_LookStart).magnitude < Screen.height * 0.03f) { tap = true; tapPos = tch.position; }
                        m_LookFinger = -1;
                    }
                }
            }
        }

        void ToggleMenu()
        {
            m_Menu = !m_Menu;
            if (m_Menu) Cursor.lockState = CursorLockMode.None;
        }

        void Toast(string s) { m_Toast = s; m_ToastT = 2.5f; }

        // ------------------------------------------------------------ HUD
        GUIStyle m_Box, m_Lbl, m_Big, m_Btn;
        void OnGUI()
        {
            float u = Mathf.Max(1f, Screen.height / 720f);
            if (m_Box == null)
            {
                var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.05f, 0.04f, 0.035f, 0.72f)); bg.Apply();
                m_Box = new GUIStyle(GUI.skin.box) { normal = { background = bg }, alignment = TextAnchor.UpperLeft };
                m_Lbl = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                m_Big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                m_Btn = new GUIStyle(GUI.skin.button);
            }
            m_Lbl.fontSize = (int)(15 * u); m_Big.fontSize = (int)(26 * u); m_Btn.fontSize = (int)(15 * u);

            if (m_Loading)
            {
                GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, m_Box);
                GUI.Label(new Rect(0, 0, Screen.width, Screen.height), m_Status, m_Big);
                return;
            }

            // task panel
            float w = 330 * u, lh = 24 * u;
            float h = (3 + m_Tasks.Count) * lh + 10 * u;
            GUI.Box(new Rect(10 * u, 10 * u, w, h), GUIContent.none, m_Box);
            GUI.Label(new Rect(20 * u, 14 * u, w - 20 * u, lh), $"<b>{m_Room.name}</b>  <color=#c9a46a>#{m_Index + 1}/{m_Rooms.Length}</color>", m_Lbl);
            GUI.Label(new Rect(20 * u, 14 * u + lh, w - 20 * u, lh), $"<color=#e8b04a>● {m_Coins} coins</color>", m_Lbl);
            for (int i = 0; i < m_Tasks.Count; i++)
            {
                var t = m_Tasks[i];
                string s = t.Done ? $"<color=#7fcf8a>✔ {t.label}</color>" : $"☐ {t.label}  <color=#c9a46a>({t.items.Count - t.Left}/{t.items.Count})</color>";
                GUI.Label(new Rect(20 * u, 14 * u + lh * (i + 2), w - 20 * u, lh), s, m_Lbl);
            }

            // crosshair + prompt
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            GUI.Label(new Rect(cx - 10 * u, cy - 12 * u, 20 * u, 24 * u), m_Target ? "◎" : "·", m_Big);
            if (m_Target) GUI.Label(new Rect(cx - 200 * u, cy + 18 * u, 400 * u, 30 * u), Prompt(m_Target), m_Big);
            if (m_Toast != null) GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 40 * u), m_Toast, m_Big);

            // buttons (handy on touch screens)
            float bw = 110 * u, bh = 40 * u;
            if (GUI.Button(new Rect(Screen.width - bw * 2 - 20 * u, 10 * u, bw, bh), "Rooms", m_Btn)) ToggleMenu();
            if (GUI.Button(new Rect(Screen.width - bw - 10 * u, 10 * u, bw, bh), "Next ▶", m_Btn)) _ = LoadRoom(m_Index + 1);
            GUI.Label(new Rect(10 * u, Screen.height - 30 * u, Screen.width, 26 * u),
                "WASD/stick move · mouse/right-stick look · E/click/A do chore · N/B next/prev room · M rooms", m_Lbl);

            if (m_Menu)
            {
                var r = new Rect(Screen.width * 0.1f, Screen.height * 0.1f, Screen.width * 0.8f, Screen.height * 0.8f);
                GUI.Box(r, GUIContent.none, m_Box);
                int cols = Mathf.Max(2, (int)(r.width / (200 * u)));
                float cw = (r.width - 30 * u) / cols, ch = 34 * u;
                int rows = Mathf.CeilToInt(m_Rooms.Length / (float)cols);
                m_MenuScroll = GUI.BeginScrollView(new Rect(r.x + 5 * u, r.y + 5 * u, r.width - 10 * u, r.height - 10 * u), m_MenuScroll, new Rect(0, 0, r.width - 30 * u, rows * ch));
                for (int i = 0; i < m_Rooms.Length; i++)
                {
                    bool done = PlayerPrefs.GetInt("lrvr_done_" + m_Rooms[i].id, 0) == 1;
                    if (GUI.Button(new Rect((i % cols) * cw, (i / cols) * ch, cw - 4, ch - 4), (done ? "✔ " : "") + m_Rooms[i].name, m_Btn)) _ = LoadRoom(i);
                }
                GUI.EndScrollView();
            }
        }

        static string Prompt(Interactable it)
        {
            switch (it.type)
            {
                case "pillow": return "Put pillow back";
                case "book": return "Put book back";
                case "frame": return "Straighten frame";
                case "lamp": return "Turn lamp off";
                case "tv": return "Turn TV off";
                case "plant": return "Water plant";
                case "trash": return "Pick up trash";
            }
            return it.type;
        }

        static void Shuffle<T>(IList<T> l) { for (int i = l.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (l[i], l[j]) = (l[j], l[i]); } }
        static List<T> Shuffled<T>(IEnumerable<T> e) { var l = e.ToList(); Shuffle(l); return l; }

        void OnDestroy() { Unload(); }
    }
}
