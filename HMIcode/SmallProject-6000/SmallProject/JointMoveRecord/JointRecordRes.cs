using Newtonsoft.Json;
using SmallProject.Logger;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace SmallProject.JointMoveRecord
{
    public static class JointRecordRes
    {
        private static readonly object FileLock = new object();
        private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "DATA");
        private static readonly string RecordFile = Path.Combine(DataDirectory, "JointRecordModel.data");

        public static ObservableCollection<JointRecordModel> Read()
        {
            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    if (!File.Exists(RecordFile))
                        return new ObservableCollection<JointRecordModel>();

                    string json = File.ReadAllText(RecordFile, Encoding.UTF8);
                    return JsonConvert.DeserializeObject<ObservableCollection<JointRecordModel>>(json)
                           ?? new ObservableCollection<JointRecordModel>();
                }
                catch (Exception ex)
                {
                    JLog.Error(ex);
                    return new ObservableCollection<JointRecordModel>();
                }
            }
        }

        public static bool Write(ObservableCollection<JointRecordModel> records)
        {
            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    string temporaryFile = RecordFile + ".tmp";
                    string json = JsonConvert.SerializeObject(records, Formatting.Indented);
                    File.WriteAllText(temporaryFile, json, new UTF8Encoding(false));
                    File.Move(temporaryFile, RecordFile, true);
                    return true;
                }
                catch (Exception ex)
                {
                    JLog.Error(ex);
                    return false;
                }
            }
        }
    }
}
