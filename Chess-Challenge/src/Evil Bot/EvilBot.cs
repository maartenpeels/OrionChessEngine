using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChessChallenge.Example
{
    public class EvilBot : IChessBot
    {
        // Super ugly, but saves tokens by not having to pass these around
        private Board? _board;
        private Timer? _timer;
        private int _maxMoveTime;

        // _, pawn, knight, bishop, rook, queen, king
        private readonly int[] _pieceValues = { 0, 100, 320, 330, 500, 900, 1000 };

        // 6 Piece-Square Tables
        // Values range from -50 to 50
        // Add 50 so that values range from 0 to 100
        // 100 can be stored in 7 bits
        // 6 * 7 = 42 bits = ulong(64 bits)
        private readonly ulong[] _pieceSquareTablesEncoded =
        {
            695353180210, 354440316210, 354440317490, 12185111090, 12185111090, 354440317490, 354440316210,
            695353180210, 698048185700, 357145808740, 357145811300,
            13548427620, 13548427620, 357145811300, 357145808740, 698048185700, 698027215420, 357124839740,
            358467100230, 14869799120, 14869799120, 358467100230,
            357124839740, 698027215420, 699369392695, 357124922295, 358467100860, 14869799755, 14869799755,
            358467100860, 357124922295, 699369392695, 1044308953650,
            700722223410, 702064566450, 358467183430, 358467183430, 702064566450, 700722223410, 1042966776370,
            1385221982775, 1045661948845, 1045661949480, 1045661950130,
            1045661950130, 1045661949480, 1044319771565, 1385221982775, 2416014132535, 2418709221180, 1732856551740,
            1731514375070, 1731514375070, 1731514374460,
            2418709221180, 2416014132535, 2413340098610, 2759622001970, 2072427235890, 1730182515250, 1730182515250,
            2072427235890, 2759622001970, 2413340098610
        };

        private readonly int[][] _pieceSquareTables = new int[7][];

        private enum NodeTypes : byte
        {
            Exact,
            LowerBound,
            UpperBound
        }

        private readonly Dictionary<ulong, (int score, Move move, int depth, NodeTypes type)> _transpositionTable =
            new();

        private const ulong TableSize = 10000000;

        public EvilBot()
        {
            DecodePieceSquareTables();
        }

        public Move Think(Board b, Timer t)
        {
            _maxMoveTime = (int)Math.Min((float)t.GameStartTimeMilliseconds / 35, t.MillisecondsRemaining * 0.1);
            _board = b;
            _timer = t;

            var bestMove = _board.GetLegalMoves().First();
            var depth = 1;
            var alpha = -2147483648;
            var beta = 2147483647;

            while (depth < 10 && _timer.MillisecondsElapsedThisTurn < _maxMoveTime)
            {
                var result = NegaMax(depth, 0, alpha, beta);

                // Aspiration Window
                if (result.score <= alpha || result.score >= beta)
                {
                    alpha = -2147483648;
                    beta = 2147483647;
                    continue;
                }

                bestMove = result.move;
                alpha = result.score - 30;
                beta = result.score + 30;
                depth++;
            }

            return bestMove;
        }

        private void OrderMoves(ref Span<Move> moves, Move moveToSearchFirst)
        {
            moves.Sort((a, b) => -EvaluateMove(a, moveToSearchFirst)
                .CompareTo(EvaluateMove(b, moveToSearchFirst)));
        }

        private void DecodePieceSquareTables()
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

        private int Evaluate()
        {
            var eval = 0;
            var mod = _board.IsWhiteToMove ? 1 : -1;
            var pieceLists = _board.GetAllPieceLists();

            foreach (var pl in pieceLists)
            {
                var pieceValue = 2 * _pieceValues[(int)pl.TypeOfPieceInList] * (pl.IsWhitePieceList ? 1 : -1);
                var pst = _pieceSquareTables[(int)pl.TypeOfPieceInList];
                if (!pl.IsWhitePieceList)
                    pst = pst.Reverse().ToArray();

                eval += pl.Sum(p => (pst[p.Square.Index] * mod) + pieceValue);
            }

            return eval;
        }

        private float EvaluateMove(Move move, Move searchThisMoveFirst)
        {
            if (move.Equals(searchThisMoveFirst))
                return float.MaxValue;

            var score = 0;

            if (move.IsCapture)
            {
                score = 10 * _pieceValues[(int)_board.GetPiece(move.TargetSquare).PieceType] -
                        _pieceValues[(int)_board.GetPiece(move.StartSquare).PieceType];
                score += _board.SquareIsAttackedByOpponent(move.TargetSquare) ? -100 : 100;
            }

            if (!move.IsPromotion) return score;

            _board.MakeMove(move);
            score += _pieceValues[(int)_board.GetPiece(move.TargetSquare).PieceType];
            _board.UndoMove(move);

            return score;
        }

        private (Move move, int score) NegaMax(int depth, int ply, int alpha, int beta)
        {
            if (ply > 0 && _board.IsRepeatedPosition())
                return (Move.NullMove, 0);

            var originalAlpha = alpha;
            var ttIndex = _board.ZobristKey % TableSize;
            var moveToSearchFirst = Move.NullMove;

            if (_transpositionTable.TryGetValue(ttIndex, out var ttEntry))
            {
                if (ttEntry.depth >= depth)
                {
                    switch (ttEntry.type)
                    {
                        case NodeTypes.Exact:
                            return (ttEntry.move, ttEntry.score);
                        case NodeTypes.LowerBound:
                            alpha = Math.Max(alpha, ttEntry.score);
                            break;
                        case NodeTypes.UpperBound:
                            beta = Math.Min(beta, ttEntry.score);
                            break;
                    }

                    if (alpha >= beta)
                        return (ttEntry.move, ttEntry.score);
                }

                moveToSearchFirst = ttEntry.move;
            }

            if (depth == 0)
                return (Move.NullMove, Quiescence(alpha, beta));

            if (_board.IsInCheckmate())
                return (Move.NullMove, -9999);

            if (_board.IsDraw())
                return (Move.NullMove, 0);

            if (_board.IsInCheck() && depth < 20)
                depth++;

            Span<Move> moves = stackalloc Move[256];
            _board.GetLegalMovesNonAlloc(ref moves);
            OrderMoves(ref moves, moveToSearchFirst);

            var bestScore = -1000;
            var bestMove = moves[0];

            foreach (var move in moves)
            {
                _board.MakeMove(move);
                var score = -NegaMax(depth - 1, ply + 1, -beta, -alpha).score;
                _board.UndoMove(move);

                if (bestScore < score)
                {
                    bestScore = score;
                    bestMove = move;
                }

                alpha = Math.Max(alpha, bestScore);

                if (_timer.MillisecondsElapsedThisTurn > _maxMoveTime)
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

        private int Quiescence(int alpha, int beta)
        {
            var standPat = _board.IsWhiteToMove ? Evaluate() : -Evaluate();

            if (standPat >= beta)
                return beta;

            if (alpha < standPat)
                alpha = standPat;

            Span<Move> moves = stackalloc Move[256];
            _board.GetLegalMovesNonAlloc(ref moves, true);
            OrderMoves(ref moves, Move.NullMove);

            foreach (var move in moves)
            {
                _board.MakeMove(move);
                var score = -Quiescence(-beta, -alpha);
                _board.UndoMove(move);

                if (score >= beta)
                    return beta;

                if (score > alpha)
                    alpha = score;
            }

            return alpha;
        }
    }
}