using System;
class Program {
    static void Main() {
        string _input = "3";
        int _pos = 0;
        _pos += 3;
        int start = _pos;
        int depth = 2;
        while (_pos < _input.Length && depth > 0) {
            if (_input[_pos] == '(') depth++;
            else if (_input[_pos] == ')') depth--;
            if (depth > 0) _pos++;
        }
        string mathExpr = _input.Substring(start, _pos - start);
        if (_pos < _input.Length) _pos++;
        Console.WriteLine(" + mathExpr + ");
    }
}
