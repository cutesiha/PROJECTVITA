using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectVita.Core
{
    /// <summary>
    /// 프로젝트 CSV 규칙(1행 컬럼명, 2행 설명, 3행 허용값, 4행부터 데이터)을 읽는다.
    /// UTF-8 BOM, CRLF/LF, 따옴표 안의 쉼표와 줄바꿈, "" 이스케이프를 처리한다.
    /// 엑셀에서 저장해도 읽히도록 너그럽게 파싱한다.
    /// </summary>
    public sealed class CsvTable
    {
        /// <summary>컬럼명 행, 설명 행, 허용값 행. 데이터는 그 다음부터다.</summary>
        public const int HeaderRowCount = 3;

        public readonly string Name;
        public readonly string[] Columns;
        public readonly List<Row> Rows = new List<Row>();

        readonly Dictionary<string, int> columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public sealed class Row
        {
            /// <summary>파일에서의 행 번호 (1부터). 오류 메시지용.</summary>
            public readonly int LineNumber;
            readonly CsvTable table;
            readonly string[] cells;

            internal Row(CsvTable table, string[] cells, int lineNumber)
            {
                this.table = table;
                this.cells = cells;
                LineNumber = lineNumber;
            }

            /// <summary>컬럼 값을 앞뒤 공백을 지운 채로 돌려준다. 컬럼이 없거나 칸이 비면 빈 문자열.</summary>
            public string this[string column]
            {
                get
                {
                    if (!table.columnIndex.TryGetValue(column, out int i) || i >= cells.Length) return string.Empty;
                    return cells[i].Trim();
                }
            }
        }

        CsvTable(string name, List<string[]> records, List<int> lineNumbers)
        {
            Name = name;
            Columns = records.Count > 0 ? records[0] : Array.Empty<string>();
            for (int i = 0; i < Columns.Length; i++)
            {
                Columns[i] = Columns[i].Trim();
                if (Columns[i].Length > 0 && !columnIndex.ContainsKey(Columns[i]))
                    columnIndex.Add(Columns[i], i);
            }

            for (int r = HeaderRowCount; r < records.Count; r++)
            {
                var cells = records[r];
                if (IsBlank(cells)) continue;
                Rows.Add(new Row(this, cells, lineNumbers[r]));
            }
        }

        public bool HasColumn(string column) => columnIndex.ContainsKey(column);

        public static CsvTable Parse(string name, string text)
        {
            var records = new List<string[]>();
            var lineNumbers = new List<int>();

            if (!string.IsNullOrEmpty(text))
            {
                int pos = 0;
                int line = 1;
                // BOM이 남아 있으면 건너뛴다 (TextAsset.text는 보통 지워 주지만 확실히 한다).
                if (text[0] == '﻿') pos = 1;

                var cells = new List<string>();
                var cell = new StringBuilder();
                bool inQuotes = false;
                int recordStartLine = line;

                while (pos < text.Length)
                {
                    char c = text[pos];

                    if (inQuotes)
                    {
                        if (c == '"')
                        {
                            if (pos + 1 < text.Length && text[pos + 1] == '"') { cell.Append('"'); pos += 2; continue; }
                            inQuotes = false; pos++; continue;
                        }
                        if (c == '\n') line++;
                        if (c != '\r') cell.Append(c);
                        pos++;
                        continue;
                    }

                    switch (c)
                    {
                        case '"':
                            inQuotes = true;
                            break;
                        case ',':
                            cells.Add(cell.ToString());
                            cell.Clear();
                            break;
                        case '\r':
                            break;
                        case '\n':
                            cells.Add(cell.ToString());
                            cell.Clear();
                            records.Add(cells.ToArray());
                            lineNumbers.Add(recordStartLine);
                            cells.Clear();
                            line++;
                            recordStartLine = line;
                            break;
                        default:
                            cell.Append(c);
                            break;
                    }
                    pos++;
                }

                // 마지막 줄에 줄바꿈이 없는 경우
                if (cell.Length > 0 || cells.Count > 0)
                {
                    cells.Add(cell.ToString());
                    records.Add(cells.ToArray());
                    lineNumbers.Add(recordStartLine);
                }
            }

            return new CsvTable(name, records, lineNumbers);
        }

        static bool IsBlank(string[] cells)
        {
            foreach (var c in cells)
                if (!string.IsNullOrWhiteSpace(c)) return false;
            return true;
        }
    }
}
