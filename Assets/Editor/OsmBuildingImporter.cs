using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Simulation.EditorTools
{
    /// Simulation > Import buildings from OpenStreetMap...
    /// Pobiera obrysy budynków z OpenStreetMap (Overpass API) dla kwadratu wokół podanego punktu,
    /// przelicza je na metry względem środka mapy, szacuje liczbę mieszkańców każdego budynku
    /// i zapisuje wynik do Assets/Resources/City/buildings.csv (czyta go CityLayer).
    ///
    /// Szacunek mieszkańców (uproszczenie): powierzchnia użytkowa = obrys × kondygnacje × 0,75,
    /// mieszkańcy = powierzchnia użytkowa / m² na osobę (NSP 2021: 1121,3 mln m² / 37,0 mln osób ≈ 30 m²).
    /// Budynki niemieszkalne (sklepy, biura, przemysł, szkoły...) mają 0 mieszkańców: scenariusz nocny.
    public class OsmBuildingImporter : EditorWindow
    {
        private const string OutputFolder = "Assets/Resources/City";
        private const string OutputFile = OutputFolder + "/buildings.csv";

        private static readonly string[] Endpoints =
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
        };

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // Domyślnie: centrum Łodzi (pl. Wolności), kwadrat 10 km = teren 100 j. przy 100 m/j.
        [SerializeField] private string placeName = "Łódź";
        [SerializeField] private double centerLat = 51.7769;
        [SerializeField] private double centerLon = 19.4547;
        [SerializeField] private float sizeKm = 10f;
        [SerializeField] private float m2PerPerson = 30f;
        [SerializeField] private float usableAreaFactor = 0.75f;
        [SerializeField] private float metersPerLevel = 3f;

        private UnityWebRequest request;
        private Process curl;
        private string curlOutput;
        private int endpointIndex;
        private bool useCurl;
        private string status = "";
        private bool Busy => request != null || curl != null;

        [MenuItem("Simulation/Import buildings from OpenStreetMap...")]
        public static void Open()
        {
            var w = GetWindow<OsmBuildingImporter>(true, "Import budynków (OSM)");
            w.minSize = new Vector2(460, 470);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Obszar", EditorStyles.boldLabel);
            placeName = EditorGUILayout.TextField("Nazwa (do opisu)", placeName);
            centerLat = EditorGUILayout.DoubleField("Środek – szerokość (lat)", centerLat);
            centerLon = EditorGUILayout.DoubleField("Środek – długość (lon)", centerLon);
            sizeKm = EditorGUILayout.Slider("Bok kwadratu [km]", sizeKm, 1f, 20f);
            EditorGUILayout.HelpBox(
                "Teren w scenie ma 100 jednostek. Przy 100 m na jednostkę (domyślne metersPerUnit) to 10 km. " +
                "Większy obszar = więcej budynków i dłuższe pobieranie (10 km w dużym mieście: ok. 1–3 min).",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Szacunek mieszkańców", EditorStyles.boldLabel);
            m2PerPerson = EditorGUILayout.FloatField("m² na osobę", m2PerPerson);
            usableAreaFactor = EditorGUILayout.Slider("Pow. użytkowa / obrys×kondygnacje", usableAreaFactor, 0.4f, 1f);
            metersPerLevel = EditorGUILayout.FloatField("Wysokość kondygnacji [m]", metersPerLevel);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Busy))
            {
                if (GUILayout.Button(Busy ? "Pobieranie..." : "Pobierz budynki", GUILayout.Height(32)))
                {
                    useCurl = false;
                    StartDownload(0);
                }
            }
            if (Busy && GUILayout.Button("Anuluj")) Cancel("Anulowano.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gdy pobieranie nie działa", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Busy))
            {
                if (GUILayout.Button("1. Kopiuj zapytanie (do overpass-turbo.eu)"))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildQuery();
                    status = "Skopiowano. Otwórz https://overpass-turbo.eu, wklej zapytanie, kliknij Uruchom, " +
                             "potem Eksport → \"raw data directly from Overpass API\" i zapisz plik .json.";
                }
                if (GUILayout.Button("2. Wczytaj pobrany plik .json"))
                {
                    string path = EditorUtility.OpenFilePanel("Plik z Overpass (JSON)", "", "json");
                    if (!string.IsNullOrEmpty(path)) ProcessText(File.ReadAllText(path));
                }
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.Info);

            EditorGUILayout.HelpBox("Dane © współtwórcy OpenStreetMap (licencja ODbL).", MessageType.None);
        }

        // ------------------------------------------------------------------ pobieranie

        private string BuildQuery()
        {
            double half = sizeKm * 500.0;
            double dLat = half / 110540.0;
            double dLon = half / (111320.0 * Math.Cos(centerLat * Math.PI / 180.0));
            string bbox = string.Format(CI, "{0:F6},{1:F6},{2:F6},{3:F6}",
                centerLat - dLat, centerLon - dLon, centerLat + dLat, centerLon + dLon);
            return "[out:json][timeout:300][maxsize:1073741824][bbox:" + bbox + "];way[\"building\"];out tags geom;";
        }

        private void StartDownload(int endpoint)
        {
            endpointIndex = endpoint;
            string query = BuildQuery();

            if (useCurl)
            {
                StartCurl(Endpoints[endpoint], query);
                return;
            }

            var form = new WWWForm();
            form.AddField("data", query);

            request = UnityWebRequest.Post(Endpoints[endpoint], form);
            request.timeout = 400;
            request.SendWebRequest();
            status = "Pobieranie z " + Endpoints[endpoint] + "...";
            EditorApplication.update += Poll;
        }

        /// Zapasowo: curl.exe (wbudowany w Windows 10/11) korzysta z certyfikatów systemu Windows,
        /// więc działa także wtedy, gdy UnityWebRequest zgłasza "Unable to complete SSL connection".
        private void StartCurl(string url, string query)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "osm_query.txt");
            curlOutput = Path.Combine(Path.GetTempPath(), "osm_buildings.json");
            File.WriteAllText(tmp, "data=" + Uri.EscapeDataString(query), new UTF8Encoding(false));
            if (File.Exists(curlOutput)) File.Delete(curlOutput);

            var psi = new ProcessStartInfo
            {
                FileName = "curl",
                Arguments = $"-sS -L --fail --max-time 400 -o \"{curlOutput}\" --data-binary \"@{tmp}\" " +
                            $"-H \"Content-Type: application/x-www-form-urlencoded\" \"{url}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            try
            {
                curl = Process.Start(psi);
                status = "Pobieranie przez curl z " + url + "...";
                EditorApplication.update += PollCurl;
            }
            catch (Exception e)
            {
                curl = null;
                status = "Nie udało się uruchomić curl: " + e.Message +
                         "\nUżyj przycisków poniżej (overpass-turbo + wczytanie pliku).";
            }
        }

        private void PollCurl()
        {
            if (curl == null) { EditorApplication.update -= PollCurl; return; }
            if (!curl.HasExited)
            {
                long bytes = File.Exists(curlOutput) ? new FileInfo(curlOutput).Length : 0;
                if (EditorUtility.DisplayCancelableProgressBar("OpenStreetMap (curl)",
                        string.Format(CI, "Pobrano {0:F1} MB...", bytes / (1024f * 1024f)), 0.5f))
                    Cancel("Anulowano.");
                return;
            }

            EditorApplication.update -= PollCurl;
            EditorUtility.ClearProgressBar();
            int code = curl.ExitCode;
            string err = curl.StandardError.ReadToEnd();
            curl.Dispose();
            curl = null;

            if (code != 0 || !File.Exists(curlOutput))
            {
                if (endpointIndex + 1 < Endpoints.Length)
                {
                    Debug.LogWarning("[OSM] curl: " + err.Trim() + " – próbuję zapasowego serwera.");
                    StartDownload(endpointIndex + 1);
                }
                else
                {
                    status = "Błąd pobierania (curl, kod " + code + "): " + err.Trim() +
                             "\nUżyj przycisków poniżej: zapytanie w overpass-turbo.eu i wczytanie pliku.";
                }
                Repaint();
                return;
            }

            ProcessText(File.ReadAllText(curlOutput));
        }

        private void Poll()
        {
            if (request == null) { EditorApplication.update -= Poll; return; }

            if (!request.isDone)
            {
                float mb = request.downloadedBytes / (1024f * 1024f);
                if (EditorUtility.DisplayCancelableProgressBar("OpenStreetMap",
                        string.Format(CI, "Pobrano {0:F1} MB...", mb), 0.5f))
                    Cancel("Anulowano.");
                return;
            }

            EditorApplication.update -= Poll;
            EditorUtility.ClearProgressBar();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string err = request.error + " (" + request.responseCode + ")";
                request.Dispose();
                request = null;
                if (endpointIndex + 1 < Endpoints.Length)
                {
                    Debug.LogWarning("[OSM] " + err + " – próbuję zapasowego serwera.");
                    StartDownload(endpointIndex + 1);
                }
                else
                {
                    Debug.LogWarning("[OSM] " + err + " – próbuję przez curl (certyfikaty Windows).");
                    useCurl = true;
                    StartDownload(0);
                }
                Repaint();
                return;
            }

            string json = request.downloadHandler.text;
            request.Dispose();
            request = null;
            ProcessText(json);
        }

        private void ProcessText(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"elements\"", StringComparison.Ordinal) < 0)
            {
                status = "Plik nie zawiera danych Overpass (brak \"elements\"). " +
                         "W overpass-turbo wybierz Eksport → \"raw data directly from Overpass API\", nie GeoJSON.";
                Repaint();
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("OpenStreetMap", "Przetwarzanie budynków...", 0.9f);
                int n = ProcessAndSave(json, out double residents);
                status = string.Format(CI,
                    "Zapisano {0:N0} budynków, szacunkowo {1:N0} mieszkańców → {2}\n" +
                    "Teraz: Simulation > Add Results HUD + Visuals (doda warstwę miasta do sceny).",
                    n, residents, OutputFile);
                Debug.Log("[OSM] " + status);
            }
            catch (Exception e)
            {
                status = "Błąd przetwarzania: " + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void Cancel(string message)
        {
            EditorApplication.update -= Poll;
            EditorApplication.update -= PollCurl;
            EditorUtility.ClearProgressBar();
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (curl != null)
            {
                try { if (!curl.HasExited) curl.Kill(); } catch (Exception) { }
                curl.Dispose();
                curl = null;
            }
            status = message;
            Repaint();
        }

        private void OnDestroy() => Cancel("");

        // ------------------------------------------------------------------ przetwarzanie

        private int ProcessAndSave(string json, out double totalResidents)
        {
            double cosLat = Math.Cos(centerLat * Math.PI / 180.0);
            var sb = new StringBuilder(1 << 22);
            sb.AppendLine(string.Format(CI, "# place={0};lat={1:F6};lon={2:F6};sizeKm={3:F2};m2PerPerson={4:F1};date={5:yyyy-MM-dd};source=OpenStreetMap (ODbL)",
                placeName.Replace(';', ','), centerLat, centerLon, sizeKm, m2PerPerson, DateTime.Now));
            sb.AppendLine("x_m,z_m,area_m2,length_m,width_m,angle_deg,height_m,residents,kind");

            int count = 0;
            totalResidents = 0;
            var pts = new List<Vector2>(64);

            var reader = new MiniJson(json);
            foreach (var element in reader.EnumerateArray("elements"))
            {
                if (!(element is Dictionary<string, object> el)) continue;
                if (!el.TryGetValue("geometry", out object geomObj) || !(geomObj is List<object> geom) || geom.Count < 3) continue;
                var tags = el.TryGetValue("tags", out object t) ? t as Dictionary<string, object> : null;

                pts.Clear();
                foreach (var g in geom)
                {
                    if (!(g is Dictionary<string, object> node)) continue;
                    if (!node.TryGetValue("lat", out object la) || !node.TryGetValue("lon", out object lo)) continue;
                    double lat = Convert.ToDouble(la, CI), lon = Convert.ToDouble(lo, CI);
                    pts.Add(new Vector2((float)((lon - centerLon) * cosLat * 111320.0), (float)((lat - centerLat) * 110540.0)));
                }
                if (pts.Count < 3) continue;

                Footprint(pts, out Vector2 centroid, out float area, out float length, out float width, out float angleDeg);
                if (area < 4f) continue;

                string type = Tag(tags, "building") ?? "yes";
                Classify(type, out byte kind, out float defaultLevels, out float residentialShare);

                float levels = ParseFloat(Tag(tags, "building:levels"), 0f);
                float height = ParseFloat(Tag(tags, "height"), 0f);
                if (levels <= 0f) levels = height > 0f ? Mathf.Max(1f, Mathf.Round(height / metersPerLevel)) : defaultLevels;
                if (height <= 0f) height = levels * metersPerLevel + (levels >= 1f ? 1f : 0f);
                levels = Mathf.Clamp(levels, 1f, 60f);

                // Małe przybudówki (garaże, komórki) nie mają mieszkańców.
                float residents = area < 25f ? 0f
                    : area * levels * usableAreaFactor / Mathf.Max(5f, m2PerPerson) * residentialShare;

                sb.Append(centroid.x.ToString("F1", CI)).Append(',')
                  .Append(centroid.y.ToString("F1", CI)).Append(',')
                  .Append(area.ToString("F0", CI)).Append(',')
                  .Append(length.ToString("F1", CI)).Append(',')
                  .Append(width.ToString("F1", CI)).Append(',')
                  .Append(angleDeg.ToString("F1", CI)).Append(',')
                  .Append(height.ToString("F1", CI)).Append(',')
                  .Append(residents.ToString("F2", CI)).Append(',')
                  .Append(kind).Append('\n');

                totalResidents += residents;
                count++;
            }

            Directory.CreateDirectory(OutputFolder);
            File.WriteAllText(OutputFile, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutputFile);
            return count;
        }

        /// Pole (wzór Gaussa), środek ciężkości i prostokąt zorientowany wzdłuż najdłuższej krawędzi.
        private static void Footprint(List<Vector2> p, out Vector2 centroid, out float area,
            out float length, out float width, out float angleDeg)
        {
            int n = p.Count;
            if (p[0] == p[n - 1]) n--;   // zamknięty wielokąt: ostatni punkt = pierwszy

            double a2 = 0, cx = 0, cy = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 u = p[i], v = p[(i + 1) % n];
                double cross = (double)u.x * v.y - (double)v.x * u.y;
                a2 += cross;
                cx += (u.x + v.x) * cross;
                cy += (u.y + v.y) * cross;
            }
            area = (float)Math.Abs(a2 * 0.5);
            centroid = Math.Abs(a2) > 1e-6
                ? new Vector2((float)(cx / (3.0 * a2)), (float)(cy / (3.0 * a2)))
                : p[0];

            float best = 0f;
            Vector2 dir = Vector2.right;
            for (int i = 0; i < n; i++)
            {
                Vector2 e = p[(i + 1) % n] - p[i];
                if (e.sqrMagnitude > best) { best = e.sqrMagnitude; dir = e; }
            }
            dir.Normalize();
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = p[i] - centroid;
                float a = Vector2.Dot(d, dir), b = Vector2.Dot(d, perp);
                minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
                minB = Mathf.Min(minB, b); maxB = Mathf.Max(maxB, b);
            }
            length = Mathf.Max(1f, maxA - minA);
            width = Mathf.Max(1f, maxB - minB);
            // Przesunięcie środka na środek prostokąta (ładniej wygląda dla kształtów L).
            centroid += dir * ((minA + maxA) * 0.5f) + perp * ((minB + maxB) * 0.5f);
            angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }

        /// kind: 0 nieznany ("yes"), 1 mieszkalny, 2 niemieszkalny, 3 przybudówka.
        private static void Classify(string type, out byte kind, out float levels, out float residentialShare)
        {
            switch (type)
            {
                case "apartments": kind = 1; levels = 5f; residentialShare = 1f; return;
                case "residential": kind = 1; levels = 4f; residentialShare = 1f; return;
                case "dormitory": kind = 1; levels = 5f; residentialShare = 1f; return;
                case "terrace": kind = 1; levels = 3f; residentialShare = 1f; return;
                case "house":
                case "detached":
                case "semidetached_house":
                case "farm": kind = 1; levels = 2f; residentialShare = 1f; return;
                case "bungalow": kind = 1; levels = 1f; residentialShare = 1f; return;
                case "garage":
                case "garages":
                case "shed":
                case "carport":
                case "roof":
                case "kiosk":
                case "hut": kind = 3; levels = 1f; residentialShare = 0f; return;
                case "yes": kind = 0; levels = 2f; residentialShare = 0.5f; return;
                default: kind = 2; levels = 2f; residentialShare = 0f; return;
            }
        }

        private static string Tag(Dictionary<string, object> tags, string key)
        {
            return tags != null && tags.TryGetValue(key, out object v) ? v as string : null;
        }

        private static float ParseFloat(string s, float fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Replace(',', '.').Replace("m", "").Trim();
            int semi = s.IndexOf(';');
            if (semi >= 0) s = s.Substring(0, semi);
            return float.TryParse(s, NumberStyles.Float, CI, out float v) ? v : fallback;
        }

        // ------------------------------------------------------------------ mały parser JSON

        /// Minimalny parser JSON. EnumerateArray zwraca kolejne elementy tablicy po jednym,
        /// żeby nie trzymać w pamięci całej odpowiedzi jako drzewa obiektów (może mieć dziesiątki MB).
        private sealed class MiniJson
        {
            private readonly string s;
            private int i;

            public MiniJson(string text) { s = text; }

            public IEnumerable<object> EnumerateArray(string key)
            {
                int k = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (k < 0) yield break;
                i = s.IndexOf('[', k);
                if (i < 0) yield break;
                i++;
                while (true)
                {
                    SkipWs();
                    if (i >= s.Length || s[i] == ']') yield break;
                    yield return ParseValue();
                    SkipWs();
                    if (i < s.Length && s[i] == ',') i++;
                }
            }

            private void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            private object ParseValue()
            {
                SkipWs();
                char c = s[i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { i += 4; return true; }
                if (c == 'f') { i += 5; return false; }
                if (c == 'n') { i += 4; return null; }
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                i++;
                while (true)
                {
                    SkipWs();
                    if (s[i] == '}') { i++; return d; }
                    string key = ParseString();
                    SkipWs();
                    i++; // ':'
                    d[key] = ParseValue();
                    SkipWs();
                    if (s[i] == ',') i++;
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                i++;
                while (true)
                {
                    SkipWs();
                    if (s[i] == ']') { i++; return list; }
                    list.Add(ParseValue());
                    SkipWs();
                    if (s[i] == ',') i++;
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                i++; // '"'
                while (true)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private double ParseNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CI);
            }
        }
    }
}
