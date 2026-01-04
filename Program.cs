using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BZIP2
{
    class Program
    {
        static void Main(string[] args)
        {
            // Damit Sonderzeichen sauber in der Konsole dargestellt werden können
            Console.OutputEncoding = Encoding.UTF8;


            Console.WriteLine("Eingabeart wählen:");
            Console.WriteLine("  1: Tastatur         2: Datei");
            Console.Write("Auswahl: ");
            var inputMode = Console.ReadLine();

            string Text;
            if (inputMode == "2")
            {
                // Text aus Datei lesen
                Console.Write("Dateipfad: ");
                var filePath = Console.ReadLine() ?? "";
                Text = File.ReadAllText(filePath);
            }
            else
            {
                // Text direkt von der Tastatur lesen
                Console.WriteLine("Text eingeben:");
                Text = Console.ReadLine() ?? "";
            }

            // Sentinel/Endmarker für die BWT: Das Zeichen \0 kommt normalerweise in Text nicht vor und macht das Ende eindeutig.
            char endMarker = '\u0000';

            Console.WriteLine("\n--- Originaltext ---");
            Console.WriteLine(Text);
            Console.WriteLine($"Länge: {Text.Length}");


            Console.WriteLine("\n====================");
            Console.WriteLine("1: Burrows–Wheeler-Transformation (BWT)");
            Console.WriteLine("====================");

            // Endmarker anhängen, damit die Rücktransformierbarkeit (eindeutiges Ende) möglich wäre
            var bwtSource = Text + endMarker;

            Console.WriteLine($"BWT-Eingabe: \"{EscapeForPrint(bwtSource)}\"");

            // lastColumn = letzte Spalte der sortierten Rotationsmatrix
            // rowIndex   = Zeile, in der die Originalrotation (ohne Verschiebung) steht
            var (lastColumn, rowIndex) = RunBwtWithLog(bwtSource);

            Console.WriteLine("\n--- BWT-Ergebnis ---");
            Console.WriteLine($"Last-Column L: \"{EscapeForPrint(lastColumn)}\"");
            Console.WriteLine($"Primary Index: {rowIndex}");


            Console.WriteLine("\n====================");
            Console.WriteLine("2: Move-To-Front (MTF)");
            Console.WriteLine("====================");

            // Alphabet für MTF
            var mtfAlphabet = lastColumn.Distinct().OrderBy(c => c).ToList();
            Console.WriteLine($"Alphabet: {FormatCharList(mtfAlphabet)}");

            // Ausgabe ist eine Liste von Indizes (Positionsnummern in der dynamischen Liste)
            var mtfOutput = RunMtfWithLog(lastColumn, mtfAlphabet);

            Console.WriteLine("\n--- MTF-Ergebnis ---");
            Console.WriteLine("Indizes: " + string.Join(", ", mtfOutput));
            Console.WriteLine("Hex: " + ToHex(mtfOutput.Select(i => (byte)i).ToArray()));


            Console.WriteLine("\n====================");
            Console.WriteLine("3: Huffman Coding");
            Console.WriteLine("====================");

            //Codierung der MTF-Indizes als Bytefolge (wie nach MTF üblich)
            var mtfByteStream = mtfOutput.Select(i => (byte)i).ToArray();
            RunHuffmanWithLog(mtfByteStream);

        }

        
        // BWT: Rotationen bilden, sortieren, Last-Column und Primary Index bestimmen
        static (string lastColumn, int rowIndex) RunBwtWithLog(string input)
        {
            int n = input.Length;
            var rotations = new List<string>(n);

            Console.WriteLine("\n-- Rotationen --");
            for (int i = 0; i < n; i++)
            {
                // Rotation i: input[i..] + input[..i]  (z.B.: ABCD -> Rotation 1 = BCDA)
                string rotation = input.Substring(i) + input.Substring(0, i);
                rotations.Add(rotation);

                // Ausgabe der Rotation (mit Escaping für Steuerzeichen)
                Console.WriteLine($"{i,2}: \"{EscapeForPrint(rotation)}\"");
            }

            Console.WriteLine("\n-- Sortierte Rotationen --");
            // Merkt zusätzlich den ursprünglichen Rotationsindex (idx), um später den Primary Index (wo steht das Original) zu finden.
            var sorted = rotations
                .Select((rot, idx) => new { rot, idx })
                .OrderBy(x => x.rot, StringComparer.Ordinal) // deterministisch (Codepoint/Ordinal)
                .ToList();

            int primaryRow = -1;
            var lastCol = new StringBuilder();

            for (int row = 0; row < n; row++)
            {
                // idx==0 entspricht der unrotierten Originalzeichenkette
                if (sorted[row].idx == 0)
                    primaryRow = row;

                // Letztes Zeichen jeder sortierten Rotation = Last-Column
                char lastChar = sorted[row].rot[^1];
                lastCol.Append(lastChar);

                Console.WriteLine(
                    $"{row,2}: (rot#{sorted[row].idx,2}) \"{EscapeForPrint(sorted[row].rot)}\"  last='{EscapeForPrint(lastChar)}'"
                );
            }

            return (lastCol.ToString(), primaryRow);
        }



        // MTF: Für jedes Symbol Index ausgeben und Symbol an den Listenanfang verschieben
        static List<int> RunMtfWithLog(string input, List<char> alphabet)
        {

            // Arbeitskopie der Symbol-Liste (wird dynamisch verändert)
            var symbolList = new List<char>(alphabet);
            var output = new List<int>();

            Console.WriteLine("\n-- MTF Schritt-für-Schritt --");

            for (int pos = 0; pos < input.Length; pos++)
            {
                char symbol = input[pos];

                // Position des Symbols in der aktuellen Liste
                int index = symbolList.IndexOf(symbol);

                // Index ist die eigentliche Ausgabe von MTF
                output.Add(index);

                Console.WriteLine($"\nPos {pos,2}: '{EscapeForPrint(symbol)}'");
                Console.WriteLine($"  Liste vorher: {FormatCharList(symbolList)}");
                Console.WriteLine($"  Index = {index}");

                // Move-To-Front: Symbol wird aus seiner Position entfernt und vorne eingefügt
                symbolList.RemoveAt(index);
                symbolList.Insert(0, symbol);

                Console.WriteLine($"  Liste nachher: {FormatCharList(symbolList)}");
            }

            return output;
        }


        // Huffman: Baum aufbauen (aus Frequenzen) und Bitcodes erzeugen
        class HuffmanNode
        {
            public byte? SymbolValue;

            // Häufigkeit (Gewicht) des Knotens
            public int Frequency;

            // Links = 0, Rechts = 1
            public HuffmanNode? LeftChild;
            public HuffmanNode? RightChild;

            public bool IsLeafNode => SymbolValue.HasValue;
        }

        static void RunHuffmanWithLog(byte[] data)
        {
            Console.WriteLine("\n-- Eingabedaten --");
            Console.WriteLine("Hex: " + ToHex(data));

            // 1) Frequenzen zählen
            var frequencyTable = new Dictionary<byte, int>();
            foreach (var b in data)
                frequencyTable[b] = frequencyTable.TryGetValue(b, out int f) ? f + 1 : 1;

            Console.WriteLine("\n-- Frequenzen --");
            foreach (var kv in frequencyTable.OrderBy(k => k.Key))
                Console.WriteLine($"Symbol {kv.Key,3}: {kv.Value}");

            // 2) Start-Knotenliste erstellen (ein Leaf pro Symbol)
            var nodes = frequencyTable
                .Select(kv => new HuffmanNode { SymbolValue = kv.Key, Frequency = kv.Value })
                .ToList();

            // 3) Huffman-Baum bauen: Wiederholt die zwei kleinsten Knoten nehmen und zu einem Parent verbinden.
            Console.WriteLine("\n-- Baumaufbau --");
            int step = 0;
            while (nodes.Count > 1)
            {
                // Sortieren nach Frequenz (bei Gleichstand nach Symbol für stabile Ausgabe)
                nodes = nodes.OrderBy(n => n.Frequency).ThenBy(n => n.SymbolValue ?? 256).ToList();

                var a = nodes[0];
                var b = nodes[1];
                nodes.RemoveRange(0, 2);

                // Parent: Frequenz = Summe; links/rechts für Bitzuweisung 0/1
                var parent = new HuffmanNode
                {
                    Frequency = a.Frequency + b.Frequency,
                    LeftChild = a,
                    RightChild = b
                };

                Console.WriteLine($"Schritt {step++}: combine ({DescribeNode(a)}) + ({DescribeNode(b)})");
                nodes.Add(parent);
            }

            // Root ist der letzte verbleibende Knoten
            var root = nodes[0];

            // 4) Code-Tabelle erzeugen (DFS: links=0, rechts=1)
            var codeTable = new Dictionary<byte, string>();
            GenerateCodeTable(root, "", codeTable);

            Console.WriteLine("\n-- Code-Tabelle --");
            foreach (var kv in codeTable.OrderBy(k => k.Key))
                Console.WriteLine($"Symbol {kv.Key,3} -> {kv.Value}");

            // 5) Daten codieren: jedes Byte durch seinen Bitcode ersetzen
            var bitStream = new StringBuilder();
            foreach (var b in data)
                bitStream.Append(codeTable[b]);

            Console.WriteLine("\n-- Bitstream --");
            Console.WriteLine(bitStream);

            // 6) Bitstring in Bytes packen (8-bit Blöcke); ggf. mit 0 auffüllen
            var packed = PackBitString(bitStream.ToString(), out int padBits);
            Console.WriteLine($"\nGepackt (padBits={padBits}):");
            Console.WriteLine("Hex: " + ToHex(packed));
        }

        // Rekursiver Durchlauf, um Codes zu bilden: Linker Ast => "0", rechter Ast => "1"
        static void GenerateCodeTable(HuffmanNode node, string prefix, Dictionary<byte, string> table)
        {
            if (node.IsLeafNode)
            {
                table[node.SymbolValue!.Value] = prefix.Length == 0 ? "0" : prefix;
                return;
            }

            if (node.LeftChild != null) GenerateCodeTable(node.LeftChild, prefix + "0", table);
            if (node.RightChild != null) GenerateCodeTable(node.RightChild, prefix + "1", table);
        }

        // Packt einen Bitstring ("010101...") in ein Byte-Array.
        static byte[] PackBitString(string bits, out int padBits)
        {
            padBits = (8 - (bits.Length % 8)) % 8;
            if (padBits > 0) bits += new string('0', padBits);

            var bytes = new byte[bits.Length / 8];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = 0;

                // 8 Bits zu einem Byte zusammensetzen 
                for (int j = 0; j < 8; j++)
                {
                    value <<= 1;
                    if (bits[i * 8 + j] == '1') value |= 1;
                }

                bytes[i] = value;
            }
            return bytes;
        }

        // Hilfstext für Debug-Ausgabe während des Baumaufbaus
        static string DescribeNode(HuffmanNode node)
        {
            return node.IsLeafNode
                ? $"sym={node.SymbolValue} freq={node.Frequency}"
                : $"inner freq={node.Frequency}";
        }

       

        //Steuerzeichen sichtbar machen und Hex-Ausgabe
        static string EscapeForPrint(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
                sb.Append(EscapeForPrint(c));
            return sb.ToString();
        }

        
        static string EscapeForPrint(char c)
        {
            if (c == '\0') return "\\0";
            if (c == '\n') return "\\n";
            if (c == '\r') return "\\r";
            if (c == '\t') return "\\t";
            if (char.IsControl(c)) return $"\\u{(int)c:X4}";
            return c.ToString();
        }

        // Formatiert eine Liste von Zeichen kompakt: [a b c ...]
        static string FormatCharList(List<char> list)
        {
            return "[" + string.Join(" ", list.Select(EscapeForPrint)) + "]";
        }

        // Bytefolge als Hex-String (z.B. "0A FF 3C")
        static string ToHex(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
