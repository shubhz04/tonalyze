namespace DiiBox
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System.Diagnostics;
    using System.Text.RegularExpressions;


    class Session
    {
        public string id                = "";
        public string path              = "";

        public string title             = "";
        public string tuning_frequency  = "";
        public string tonal_key         = "";
        public double rawBpm            = 0;
        public int    bpm               = 0;

        private string jsonData         = "";


        public void Initialize()
        {
            id = DateTime.Now.ToString("yyyyMMddhhmmss");
            path = Environment.CurrentDirectory + @"\sessions\" + id + @"\";
            Directory.CreateDirectory(path);
        }
        public void SetSessionData(string _title, string _tuningFreq, string _tonalKey, double _rawBpm, int _bpm)
        {
            title = _title;
            tuning_frequency = _tuningFreq;
            tonal_key = _tonalKey;
            rawBpm = _rawBpm;
            bpm = _bpm;
            this.jsonData = JsonConvert.SerializeObject(this, Formatting.Indented);
        }
        public string GetJson() => jsonData;
        public void Save()
        {
            if (jsonData == "")
                return;
            File.WriteAllText(this.path + @"\session.json", this.jsonData);
        }
    }
    class App
    {
        #region CONSTS
        const string TONAL_DETECTOR_FILENAME = "key.exe";
        const string RHYTHM_DETECTOR_FILENAME = "rhythm.exe";
        const string YTDLP_FILENAME = "ytdlp.exe";
        const string FFPROBE_FILENAME = "ffprobe.exe";
        const string DASHED_SEPERATOR = "-----------------------";
        #endregion

        static Session? currentSession;
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please provide a YouTube, SoundCloud or Instagram link as an argument. \n Exiting...");
                return -1;
            }

            // checks with the followin format
            // arg2= source_link (youtube or soundcloud or insta reels whatever)
            // [optional] arg2 = download or analyze (if -a then only run the analyzers) will add this later)
            string source_link = args[0];

            currentSession = new Session();
            currentSession.Initialize();

            ProcessStartInfo ytdlp_processinfo         = new ()
            {
                FileName = Environment.CurrentDirectory + @"\bin\" + YTDLP_FILENAME,
                Arguments = source_link + @" --restrict-filenames -q --no-warnings --newline --progress --extract-audio --write-info-json --audio-format mp3 --audio-quality 320K -o " + currentSession.path + @"\ref.mp3",
            };
            ProcessStartInfo ffmpeg_processinfo        = new ()
            {
                FileName = Environment.CurrentDirectory + @"\bin\ffmpeg.exe",
                WorkingDirectory =  currentSession.path,
                Arguments = "-i ref.mp3 -vn -acodec copy -to 00:00:45 data.mp3 -nostats -loglevel 0",
            };
            ProcessStartInfo ffprobe_processinfo       = new ()
            {
                FileName = Environment.CurrentDirectory + @"\bin\ffprobe.exe",
                WorkingDirectory =  currentSession.path,
                Arguments = "-v error -hide_banner  -show_entries stream=duration -of default=noprint_wrappers=1 ref.mp3",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };
            ProcessStartInfo tonaldetector_processinfo = new ()
            {
                FileName = Environment.CurrentDirectory + @"\bin\" + TONAL_DETECTOR_FILENAME,
                WorkingDirectory =  currentSession.path,
                Arguments = " data.mp3 tonal.yaml",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };
            ProcessStartInfo rhythm_processinfo        = new ()
            {
                FileName = Environment.CurrentDirectory + @"\bin\" + RHYTHM_DETECTOR_FILENAME,
                WorkingDirectory =  currentSession.path,
                Arguments = " data.mp3 ",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };


            // process declaration
            Process ytdlp_process   =  new () { StartInfo = ytdlp_processinfo         };
            Process ffmpeg_process  =  new () { StartInfo = ffmpeg_processinfo        };
            Process ffprobe_process =  new () { StartInfo = ffprobe_processinfo       };

            Process tonal_process   =  new () { StartInfo = tonaldetector_processinfo };
            Process rhythm_process  =  new () { StartInfo = rhythm_processinfo        };



            // starts downloading from youtube
            ytdlp_process.Start();
            ytdlp_process.WaitForExit();

            // process meta-data
            string rawMetaData = File.ReadAllText(currentSession.path + @"ref.mp3.info.json");
            dynamic metadata = JObject.Parse(rawMetaData);
            string trackTitle = metadata.title;

            // checks if the track length is less than 50 seconds
            ffprobe_process.Start();
            ffprobe_process.WaitForExit();

            string raw_duration = ffprobe_process.StandardOutput.ReadToEnd().Remove(0,9);
            double track_duration = (int)Math.Round(double.Parse(raw_duration));

            if (track_duration < 60)
            {
                // just use the whole clip as the reference clip for analyzers
                ffmpeg_processinfo.Arguments = "-i ref.mp3 -vn -acodec copy -to 00:00:" + track_duration.ToString() + " data.mp3 -nostats -loglevel 0";
            }

            // post-process to make a cutdown file for faster estimation
            ffmpeg_process.Start();
            ffmpeg_process.WaitForExit();

            // print meta-data 
            Console.WriteLine(DASHED_SEPERATOR);
            Console.WriteLine("[info] Total Track Duration : " + track_duration.ToString() + "s");
            Console.WriteLine("[info] Track Title : " + trackTitle);

            // ------------------------
            // starts analyzer processes
            // in general tonal analyzer is faster than rhytm 
            tonal_process.Start();
            rhythm_process.Start();

            // check for exit code just as this analyzer exits, print the data (looks fast apparently)
            tonal_process.WaitForExit();
            if (tonal_process.ExitCode != 0)
            {
                Console.WriteLine("Error analyzing tonal key, exiting ");
                return -1;
            }
            string tonalOutput = tonal_process.StandardOutput.ReadToEnd().ReplaceLineEndings();
            string[] tonal_outDataLines = tonalOutput.Split(Environment.NewLine);
            string tuningFrequency = tonal_outDataLines[0].Remove(0,18);
            string tonalKey = tonal_outDataLines[1].Remove(0,5);

            Console.WriteLine("[info] Tuning Frequency : " + tuningFrequency);
            Console.WriteLine("[info] Tonal Key : " + tonalKey);

            // waits for the rhytm extractor to end and finalize the net results
            // will add beatmap generation here later
            rhythm_process.WaitForExit();
            if (rhythm_process.ExitCode != 0)
            {
                Console.WriteLine("Error analyzing rhythm steps, exiting ");
                return -1;
            }

            string   rhythmOutput = rhythm_process.StandardOutput.ReadToEnd().ReplaceLineEndings();
            string[] rhythm_outData_lines  = rhythmOutput.Split(Environment.NewLine);

            // parse the bpm from string and then rounds it to a whole number
            string   bpmStr   = rhythm_outData_lines[4].Remove(0,4);
            double   bpmfloat = double.Parse(bpmStr);
            int      bpmInt   = (int) Math.Round(bpmfloat);


            Console.WriteLine("[info] Raw BPM : " + bpmfloat);
            Console.WriteLine("[info] Recommended BPM : " + bpmInt);

            // cleans up the filename for illegal characters for windows filesystems
            Regex reg = new Regex("[*'\"/,_&#^@]");
            trackTitle = reg.Replace(trackTitle, string.Empty);

            // finally renames the track, to the original track name
            // and deletes the temporary cutdown data file used by analyzers
            File.Move(currentSession.path + @"ref.mp3", currentSession.path + trackTitle + @".mp3");
            File.Delete(currentSession.path + @"data.mp3");

            // create a json report to access in the future
            currentSession.SetSessionData(trackTitle, tuningFrequency, tonalKey, bpmfloat, bpmInt);
            currentSession.Save();
            Console.WriteLine(DASHED_SEPERATOR);
            Console.WriteLine("[log] session.json generated");

            // opens the explorer for further exploration
            Process.Start("explorer.exe", currentSession.path);
            return 0;
        }
    }
}
