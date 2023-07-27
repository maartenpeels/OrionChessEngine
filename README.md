# Chess Coding Challenge (C#)
This my submission for the Tiny Chess Bots coding challenge.

- Video: https://youtu.be/iScy18pVR58
- Challenge: https://github.com/SebLague/Chess-Challenge

## Interesting Stuff
### MiniMax vs NegaMax
I started of using MiniMax but switched to NegaMax because it was easier to implement/read but also used a lot less tokens.

### Piece-Square Tables
Because of the 1024 token limit the use of piece-square tables was out of the question, or so I thought.

I ended up encoding the 6 piece-square tables into a single array using some bit shifting. This can be found [here](https://github.com/maartenpeels/PetitePawnWizard/blob/main/Chess-Challenge/src/My%20Bot/PSTEncoder.py).

### Quiescence Search
The bot was constantly blundering pieces so I've added Quiescence Search to try and avoid this. You can read more about it [here](https://www.chessprogramming.org/Quiescence_Search).