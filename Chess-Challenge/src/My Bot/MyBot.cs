using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    bool _initialized;

    // _, pawn, knight, bishop, rook, queen, king
    int[] _pieceValues = { 0, 100, 320, 330, 500, 900, 1000 };

    // 6 Piece-Square Tables
    // Values range from -50 to 50
    // Add 50 so that values range from 0 to 100
    // 100 can be stored in 7 bits
    // 6 * 7 = 42 bits = ulong(64 bits)
    ulong[] _pieceSquareTablesEncoded =
        {
            695353180210,354440316210,354440317490,12185111090,12185111090,354440317490,354440316210,695353180210,698048185700,357145808740,357145811300,
            13548427620,13548427620,357145811300,357145808740,698048185700,698027215420,357124839740,358467100230,14869799120,14869799120,358467100230,
            357124839740,698027215420,699369392695,357124922295,358467100860,14869799755,14869799755,358467100860,357124922295,699369392695,1044308953650,
            700722223410,702064566450,358467183430,358467183430,702064566450,700722223410,1042966776370,1385221982775,1045661948845,1045661949480,1045661950130,
            1045661950130,1045661949480,1044319771565,1385221982775,2416014132535,2418709221180,1732856551740,1731514375070,1731514375070,1731514374460,
            2418709221180,2416014132535,2413340098610,2759622001970,2072427235890,1730182515250,1730182515250,2072427235890,2759622001970,2413340098610
        };

    int[][] _pieceSquareTables = new int[7][];

    enum NodeTypes : byte
    {
        Exact,
        LowerBound,
        UpperBound
    };
    Dictionary<ulong, (int score, Move move, int depth, NodeTypes type)> _transpositionTable = new();
    ulong _tableSize = 10000000;

    public Move Think(Board board, Timer timer)
    {
        var maxMoveTime = Math.Min(3000, (int)Math.Floor(timer.MillisecondsRemaining * 0.1));
        if (timer.MillisecondsRemaining < 10000)
            maxMoveTime = Math.Min(1000, (int)Math.Floor(timer.MillisecondsRemaining * 0.3));

        if (!_initialized)
        {
            DecodePieceSquareTables();
            _initialized = true;
        }

        var moves = board.GetLegalMoves();
        var bestMove = moves[0];

        for (var depth = 1; depth < 10 && timer.MillisecondsElapsedThisTurn < maxMoveTime; depth++)
        {
            bestMove = NegaMax(board, timer, maxMoveTime, depth, 0, int.MinValue, int.MaxValue).move;
        }

        return bestMove.IsNull ? moves[0] : bestMove;
    }

    void OrderMoves(ref Move[] moves, Board board, Move moveToSearchFirst)
    {
        Array.Sort(moves,
            (a, b) => -EvaluateMove(a, board, moveToSearchFirst)
                .CompareTo(EvaluateMove(b, board, moveToSearchFirst)));
    }

    void DecodePieceSquareTables()
    {
        for (var index = 0; index < 7; index++)
        {
            var table = new int[64];
            for (var i = 0; i < 64; i++)
            {
                table[i] = (int)((_pieceSquareTablesEncoded[index] >> (i * 7)) & 0x7F) - 50;
            }

            _pieceSquareTables[index] = table;
        }
    }

    int Evaluate(Board board)
    {
        var eval = 0;
        var mod = board.IsWhiteToMove ? 1 : -1;
        var pieceLists = board.GetAllPieceLists();

        eval += pieceLists.SelectMany(pl => pl).Sum(p =>
        {
            var pieceValue = _pieceValues[(int)p.PieceType] * (p.IsWhite ? 1 : -1);
            var pst = _pieceSquareTables[(int)p.PieceType];
            if (!p.IsWhite)
                pst = pst.Reverse().ToArray();
            var squareValue = pst[p.Square.Index] * mod;

            return pieceValue + squareValue;
        });

        return eval;
    }

    public float EvaluateMove(Move move, Board board, Move searchThisMoveFirst)
    {
        if (move.Equals(searchThisMoveFirst))
            return float.MaxValue;

        var score = 0;

        if (move.IsCapture)
        {
            score = 10 * _pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType] - _pieceValues[(int)board.GetPiece(move.StartSquare).PieceType];
            score += score < 0 && board.SquareIsAttackedByOpponent(move.TargetSquare) ? -100 : 100;
        }

        if (move.IsPromotion)
        {
            board.MakeMove(move);
            score += _pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType];
            board.UndoMove(move);
        }

        return score;
    }

    (Move move, int score) NegaMax(Board board, Timer timer, int maxMoveTime, int depth, int ply, int alpha, int beta)
    {
        if (ply > 0 && board.IsRepeatedPosition())
            return (Move.NullMove, 0);

        var originalAlpha = alpha;
        var ttIndex = board.ZobristKey % _tableSize;
        var moveToSearchFirst = Move.NullMove;

        if (_transpositionTable.ContainsKey(ttIndex))
        {
            var ttEntry = _transpositionTable[ttIndex];
            if (ttEntry.depth >= depth)
            {
                switch (ttEntry.type)
                {
                    case NodeTypes.Exact:
                        return (ttEntry.move, ttEntry.score);
                    case NodeTypes.LowerBound:
                        alpha = Math.Max(alpha, (int)ttEntry.score);
                        break;
                    case NodeTypes.UpperBound:
                        beta = Math.Min(beta, (int)ttEntry.score);
                        break;
                }

                if (alpha >= beta)
                    return (ttEntry.move, ttEntry.score);
            }

            moveToSearchFirst = ttEntry.move;
        }

        if (depth == 0)
            return (Move.NullMove, Quiescence(board, alpha, beta));

        if (board.IsInCheckmate())
            return (Move.NullMove, -9999);

        if (board.IsDraw())
            return (Move.NullMove, 0);

        var moves = board.GetLegalMoves();
        OrderMoves(ref moves, board, moveToSearchFirst);

        var bestScore = int.MinValue;
        var bestMove = Move.NullMove;

        foreach (var move in moves)
        {
            board.MakeMove(move);
            var score = -NegaMax(board, timer, maxMoveTime, depth - 1, ply + 1, -beta, -alpha).score;
            board.UndoMove(move);

            if (bestScore < score)
            {
                bestScore = score;
                bestMove = move;
            }
            alpha = Math.Max(alpha, bestScore);

            if (timer.MillisecondsElapsedThisTurn > maxMoveTime)
                break;

            if (alpha >= beta)
                break;
        }

        var nodeType = NodeTypes.Exact;
        if (bestScore <= originalAlpha)
            nodeType = NodeTypes.UpperBound;
        else if (bestScore >= beta)
            nodeType = NodeTypes.LowerBound;
        _transpositionTable[ttIndex] = (bestScore, bestMove, depth, nodeType);

        return (bestMove, bestScore);
    }

    int Quiescence(Board board, int alpha, int beta)
    {
        var standPat = board.IsWhiteToMove ? Evaluate(board) : -Evaluate(board);

        if (standPat >= beta)
            return beta;

        if (alpha < standPat)
            alpha = standPat;

        var moves = board.GetLegalMoves(true);
        OrderMoves(ref moves, board, Move.NullMove);

        foreach (var move in moves)
        {
            board.MakeMove(move);
            var score = -Quiescence(board, -beta, -alpha);
            board.UndoMove(move);

            if (score >= beta)
                return beta;

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }
}