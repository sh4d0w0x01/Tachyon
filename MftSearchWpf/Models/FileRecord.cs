namespace MftSearchWpf.Models
{
    public class FileRecord
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        public long Size { get; set; } = 0;
        public string Extension { get; set; } = string.Empty;
        public System.DateTime? DateModified { get; set; }
    }
}
