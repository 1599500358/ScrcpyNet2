using System;
using System.Collections.Generic;
using System.IO;

public class FileReader
{
    public List<string[]> ReadFile(string filePath)
    {
        List<string[]> lines = new List<string[]>();

        try
        {
            using (StreamReader sr = new StreamReader(filePath))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    // Strip a UTF-8 BOM so the first serial matches adb exactly.
                    line = line.TrimStart('\uFEFF');
                    var data = line.Split(" ");
                    if(data.Length==2)
                        lines.Add(data);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while reading the file: " + ex.Message);
        }

        return lines;
    }
}
