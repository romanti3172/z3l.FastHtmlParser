using System.Buffers;
using z3l.FastHtmlParser.Core.XPath;

namespace z3l.FastHtmlParser.Core.Markdown;

// Tables: a table of data is written as a GitHub table, any other one as the blocks in its cells.
internal ref partial struct MarkdownConverter
{
    private struct TableState
    {
        public int End;         // the last char of the table
        public int Columns;     // of its widest row
        public int Rows;        // rows written
        public int Column;      // in the open row: where the next cell goes
        public int RowEnd;      // the last char of the open row
        public bool RowOpen;
        public bool HeaderRow;  // the first row is the heading row
        public bool Spans;      // a cell has a colspan or a rowspan
    }

    private void OpenTable(int index, in ParsingCore.TagNode node, bool container)
    {
        if (_inTable) return;

        Break(Blank);
        if (!container) return;

        if (_inHeading || _cellDepth > 0 || !ScanTable(index, in node))
        {
            Push(in node, MdKind.Block);
            return;
        }

        _inTable = true;
        _gap = true;
        Push(in node, MdKind.Table);
    }

    private void CloseTable()
    {
        if (_table.RowOpen) EndRow();

        _inTable = false;
        _gap = false;
        ClearCovered();
        Break(Blank);
    }

    private void OpenRow(in ParsingCore.TagNode node, bool container)
    {
        if (!_inTable)
        {
            Break(Blank);
            if (container) Push(in node, MdKind.Block);
            return;
        }

        if (_cellDepth > 0) return;
        if (_table.RowOpen) EndRow();
        BeginRow(container ? node.End : _table.End);
    }

    private void OpenCell(in ParsingCore.TagNode node, bool container)
    {
        if (!_inTable)
        {
            Break(Blank);
            if (container) Push(in node, MdKind.Block);
            return;
        }

        if (_cellDepth > 0)
        {
            _pendingSpace = true;
            return;
        }

        if (!_table.RowOpen) BeginRow(_table.End); // a cell outside any <tr>

        int colspan = 1, rowspan = 1;
        if (_table.Spans) ReadSpans(in node, out colspan, out rowspan);

        // the columns a cell of a row above reaches down to
        int free = FirstFree(_table.Column);
        for (; _table.Column < free; _table.Column++) Write("  |");

        Write(' ');
        _cellDepth = 1;
        _gap = false;
        _cellPrefixBase = _prefixCount;
        _lineEmpty = true;
        _blockStart = false;
        _pendingSpace = false;
        _break = None;

        if (container) Push(in node, MdKind.TableCell, colspan, rowspan);
        else CloseCell(colspan, rowspan);
    }

    private void CloseCell(int colspan, int rowspan)
    {
        if (_wrapsOpened > 0) CloseWrappers();
        _pendingSpace = false;
        _break = None;
        _breakDepth = int.MaxValue;

        Write(" |");
        for (int c = 1; c < colspan; c++) Write("  |");

        if (rowspan > 1) Cover(_table.Column, colspan, rowspan);
        _table.Column += colspan;

        _cellDepth = 0;
        _gap = true;
        _lineEmpty = false;
    }

    /// <summary>&lt;caption&gt;: a paragraph before the table.</summary>
    private bool OpenCaption(in ParsingCore.TagNode node, bool container)
    {
        if (!_inTable)
        {
            Break(Blank);
            if (container) Push(in node, MdKind.Block);
            return false;
        }

        if (!container) return false;
        if (_cellDepth > 0 || _table.RowOpen || _table.Rows > 0) return true; // no place for it after the rows

        _gap = false;
        Push(in node, MdKind.Caption);
        return false;
    }

    private void BeginRow(int rowEnd)
    {
        if (_table.Rows == 0 && !_table.HeaderRow)
        {
            // a Markdown table starts with a heading row: an empty one when the table has none
            StartLine();
            WriteCells(_table.Columns, "  |");
            NewLine(blank: false);
            WriteCells(_table.Columns, " --- |");
            _break = Line;
        }

        StartLine();
        Write('|');
        _table.RowOpen = true;
        _table.RowEnd = rowEnd;
        _table.Column = 0;
        _lineEmpty = false;
        _blockStart = false;
    }

    private void EndRow()
    {
        // every row has all the columns
        for (; _table.Column < _table.Columns; _table.Column++) Write("  |");

        _table.RowOpen = false;
        UncoverRow(0);

        if (_table.Rows == 0 && _table.HeaderRow)
        {
            NewLine(blank: false);
            WriteCells(_table.Columns, " --- |");
        }

        _table.Rows++;
        _break = Line;
    }

    private void WriteCells(int count, string cell)
    {
        Ensure(1 + count * cell.Length);
        _buf[_len++] = '|';
        for (int c = 0; c < count; c++)
        {
            cell.CopyTo(_buf.AsSpan(_len));
            _len += cell.Length;
        }
    }

    // ----- rowspan -----

    /// <summary>The first column from <paramref name="column"/> on that no cell of a row above covers.</summary>
    private int FirstFree(int column)
    {
        while (column < _coveredCount && _covered[column] > 0) column++;
        return column;
    }

    /// <summary>A cell at <paramref name="column"/> also takes the place of cells in the rows below.</summary>
    private void Cover(int column, int colspan, int rowspan)
    {
        int end = column + colspan;
        if (end > _covered.Length)
        {
            var bigger = ArrayPool<int>.Shared.Rent(Math.Max(end, _covered.Length * 2));
            _covered.AsSpan(0, _coveredCount).CopyTo(bigger);
            if (_covered.Length > 0) ArrayPool<int>.Shared.Return(_covered);
            _covered = bigger;
        }

        if (end > _coveredCount)
        {
            _covered.AsSpan(_coveredCount, end - _coveredCount).Clear();
            _coveredCount = end;
        }

        for (int c = column; c < end; c++) _covered[c] = rowspan;
    }

    /// <summary>
    /// Ends a row for the cells that reach down from it and from the rows above. Returns the width of the row:
    /// <paramref name="width"/>, or more if a cell from above covers a column further to the right.
    /// </summary>
    private int UncoverRow(int width)
    {
        for (int c = 0; c < _coveredCount; c++)
        {
            if (_covered[c] <= 0) continue;

            if (c >= width) width = c + 1;
            _covered[c]--;
        }
        return width;
    }

    private void ClearCovered()
    {
        _covered.AsSpan(0, _coveredCount).Clear();
        _coveredCount = 0;
    }

    private void ReadSpans(in ParsingCore.TagNode node, out int colspan, out int rowspan)
    {
        colspan = 1;
        rowspan = 1;

        var scanner = new AttributeScanner(_source, node.Start, OpenTagEnd(in node));
        while (scanner.Next(out int nameStart, out int nameLength, out int valueStart, out int valueLength))
        {
            if (nameLength != 7 || valueStart < 0) continue;

            var name = _source.Slice(nameStart, 7);
            var value = _source.Slice(valueStart, valueLength);

            // the limits of the HTML standard
            if (name.Equals("colspan", StringComparison.OrdinalIgnoreCase)) colspan = ParseSpan(value, 1000);
            else if (name.Equals("rowspan", StringComparison.OrdinalIgnoreCase)) rowspan = ParseSpan(value, 65534);
        }
    }

    private static int ParseSpan(ReadOnlySpan<char> value, int max)
    {
        value = value.TrimStart();

        int span = 0;
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c)) break;
            span = span * 10 + (c - '0');
            if (span > max) return max;
        }
        return span == 0 ? 1 : span;
    }

    // ----- what kind of table -----

    private struct TableScan
    {
        public int Rows, Columns, Cells;
        public int Column, RowEnd, RowCells;
        public bool RowOpen, RowInHead, RowAllHeadings;
        public bool HeaderRow, Blocks, Spans;
    }

    /// <summary>
    /// Reads the table once before it is written: how many columns it has, whether its first row is a heading row,
    /// and whether it is a table of data at all. Returns false for a table that is written as plain blocks:
    /// one with a table inside or with a single column (a page layout), one row of blocks, <c>role="presentation"</c>.
    /// The rows and cells are counted exactly as they are written afterwards.
    /// </summary>
    private bool ScanTable(int index, in ParsingCore.TagNode table)
    {
        var data = _data;
        int count = data.Count;
        int end = table.End;
        int cellEnd = -1;
        var scan = new TableScan();

        for (int j = index + 1; j < count; j++)
        {
            ref readonly var node = ref data.Node(j);
            if (node.Start > end) break;

            int key = node.Key;
            if (key < 0) continue;

            var kind = MarkdownKinds.Of[key];
            if (kind == MdKind.Inline) continue;

            if (scan.RowOpen && node.Start > scan.RowEnd && node.Start > cellEnd) EndScanRow(ref scan);

            switch (kind)
            {
                case MdKind.Table:
                    ClearCovered();
                    return false;

                case MdKind.Skip:
                    while (j + 1 < count && data.Node(j + 1).Start <= node.End) j++;
                    break;

                case MdKind.TableRow:
                    if (node.Start < cellEnd) break;
                    if (scan.RowOpen) EndScanRow(ref scan);

                    BeginScanRow(ref scan, node.InnerStart > 0 && node.InnerEnd > 0 ? node.End : end);
                    scan.RowInHead = node.ParentIndex >= 0 && data.Node(node.ParentIndex).Key == (int)HtmlTag.Thead;
                    break;

                case MdKind.TableCell:
                {
                    if (node.Start < cellEnd) break;
                    if (!scan.RowOpen) BeginScanRow(ref scan, end);

                    int colspan = 1, rowspan = 1;
                    if (_source.Slice(node.Start, OpenTagEnd(in node) - node.Start).Contains("span", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadSpans(in node, out colspan, out rowspan);
                        if (colspan > 1 || rowspan > 1) scan.Spans = true;
                    }

                    scan.Column = FirstFree(scan.Column);
                    if (rowspan > 1) Cover(scan.Column, colspan, rowspan);
                    scan.Column += colspan;

                    scan.Cells++;
                    scan.RowCells++;
                    if (key != (int)HtmlTag.Th) scan.RowAllHeadings = false;
                    cellEnd = node.End;
                    break;
                }

                case MdKind.Block:
                case MdKind.Heading:
                case MdKind.Pre:
                case MdKind.Hr:
                case MdKind.List:
                case MdKind.OrderedList:
                case MdKind.ListItem:
                case MdKind.Blockquote:
                case MdKind.Term:
                case MdKind.Definition:
                    if (node.Start < cellEnd) scan.Blocks = true;
                    break;
            }
        }

        if (scan.RowOpen) EndScanRow(ref scan);
        ClearCovered();

        if (scan.Columns < 2 || (scan.Rows == 1 && !scan.HeaderRow && scan.Blocks)) return false;

        var role = Attribute(in table, "role").Trim();
        if (role.Equals("presentation", StringComparison.OrdinalIgnoreCase) || role.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;

        long empty = (long)scan.Rows * scan.Columns - scan.Cells;
        long cost = empty * 3 + scan.Columns * 12L;
        if (cost > _padBudget) return false;
        _padBudget -= cost;

        _table = new TableState { End = end, Columns = scan.Columns, HeaderRow = scan.HeaderRow, Spans = scan.Spans };
        return true;
    }

    private static void BeginScanRow(ref TableScan scan, int rowEnd)
    {
        scan.RowOpen = true;
        scan.RowEnd = rowEnd;
        scan.Column = 0;
        scan.RowCells = 0;
        scan.RowInHead = false;
        scan.RowAllHeadings = true;
    }

    private void EndScanRow(ref TableScan scan)
    {
        int width = UncoverRow(scan.Column);
        if (width > scan.Columns) scan.Columns = width;

        // the heading row: the row of a <thead>, or a first row of <th> only
        if (scan.Rows == 0) scan.HeaderRow = scan.RowInHead || (scan.RowCells > 0 && scan.RowAllHeadings);

        scan.Rows++;
        scan.RowOpen = false;
    }
}
