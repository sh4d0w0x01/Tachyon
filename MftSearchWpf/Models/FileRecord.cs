namespace MftSearchWpf.Models
{
    public class FileRecord
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        // You could add size or other properties here, but MFT USN journal
        // primarily gives FRN, ParentFRN, and FileName.
        // Size requires querying the MFT record details which slows down indexing significantly.
    }
}
