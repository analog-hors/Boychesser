from typing import Union
from io import BufferedReader
from tkinter import Tk, Canvas, Menu, Label, Toplevel, filedialog, Event
import chess, json, lzma, base64, struct

PIECE_DATA = json.loads(lzma.decompress(base64.b85decode("{Wp48S^xk9=GL@E0stWa8~^|S5YJf5;5UW|=v@Fkh=@WrDVS_<Xo_Zr<XuUl<C)FZO?bvu{v>`<1+MfMJzgiWC1&4MhMxs|=Kzz+)7-Xu7(G<Ky+_y+Q5Cc>fh6F~hA!iJrTT;q5S6iicUZs`-7)7%Nnb+6F!~ozM4UCEYl5D&fWku6^Kq0?>WbgPMZIiu*L7DMjp7doBf<AH=A+MrJLVgALdo4qwi>sCPNAi6L3~8Gi8eFMU?l!=9(`oDlxQCd9NgB8gPj~D!7KZ(EIv{yrs!T|z1_d%)@s`mco5+IxHhAjP4(ycH#8bx^hIJRPg$w?Y$v8nkou;yGCM%`^*sW}yA$8bnIx!s4vLV@sV0nD6jqn_;eogY4K_;JeD8sKOo?GJP)BZ72<^WwqE_;Vx8vrE61-S=J3p=h*~F<1TS|LZc4iH_(hV&HPFH_;i&cbS8DC@M!$JpF;2`%Oe@5~KyM6Bk)_;gCsRV1vtXo^ar7y-1jH2m>COzTfk(2C4oe<>UrqDiReN(W?idCW0gPO552&bSn!U@oecFQH3h$~u%o$<IenE2<%)$rQ*FQ@1EEwg$|CgFtT$}-F6amrbmEguMbPw<1!o<%WmBIeQc=#BqrPxZq)uJXC^FlUYx743Nka8DPcFKoXfug$=fUecRVJ4XqSy32#UBhE_Z=F3Sd)oLOW2ZRavIGPtY>^yUSXrP7Wz~9==?4N<zu9r0tKY+&aTleL({J*g*>=SxD(&*c|1^&*1z8b8DpX?Y<W-<k1^`N}+mtJ7?U9Axns-ygr^WW&4pMH_wl2O%78jiT2>#4xAI}JtQ+|cwa@isv2k3y`SNB$>u6g$e3TW0OE2bi^eTKpgJKBO^I6yp`Vyi`da@9GvTJm5`La_owN(_Zm7giYRuMgmIJQ}}5Z(z@Z)#GKQ#KdPb$bI-E1l4{})5z~Blz#-Gd{o5p?_#4#k*cRmn`LtrirgJFi_4A!SSvI#v;mWg0R&UcA>L||)!z+!CV#aOa0*cdyt4O5vHkK6-u%=xhkYsoPc58BC+hXH;WDd}0hJ^FLd8%C;=B-y^2`aWhRq#aH*S;xfe3GT5b-9#lz-WraO+`}9Bs4H3+{IbPp1R7ysE=G_RokYUIe#v=S1(`+EkBOk7P>neh!Sn4^g-GzK=U@!!s0llVr8RI|6F4iZn9-OAjs&S(C5^HLKxZ8)b!QLXhW*lXs-F!897c!gZ-x&&pgD3yqaHLIqJMZE*~zGulwdYX>}4QZte_*XkUm=M-|3vVbl#5j_5e6%-VeY<<>$G+cazHDj1dv&m$*F>twAdu=cwxH;uj;O6cp%!@`$5npQKVv_DPDdcb&Ct{FC~R*kyi_M{BTCusm^X0HOUjbTQyb=1ec^2rurUkbSOd6GIH-T3*cZh;T<T@(JoP`v3nRHCK+9E{=vaC7jTv<%cO9|aJ}fE5HR_B@~opcE9!C?LT=!Fmi4G-V(v0Mv{}@|}V^J3SLJBQV#Ly(k|HB-oF;+Dv?|t{)F@hU21k+F5Aj!wnL}yUh6s2kY>s&mLGQ2mYNhxomuhpsfivXu)gYLVF)nhdazWEQGrYA-*E!x<xg>-O~hE|4W(S4QL)|eYq%DEv?{CmavhTYX~a^v;FbrA_f$jq;oqM*G$neo0=Z$W*vnHB=%M!qoRLTi4_E>SEZ>aByo5l58iRDy`&G4jmzqc)38|bLIG(bN~WXuixzBnDoy<B1Ul27HDxU<XP~aDb&?8-g1!9AoKbC5VZssc7Y^cn!!9Dqvc@~+Sf+S67Ebjm8RAR_<tqU8b$^gkJY#WD&a&;wi6GlQqQE3#>qgvjx8Zq#8=pwCcT*7oD;Q*yx5)8U1^BL&nEk?=36^$6x({{5c1Ps~XvbIaA|B9@K+LCOAowf4<?oFoDLDJs7iq#j{lEGNAaI&`SdIjFaI}wT;2nhd1k1AxE3IWiZp&@e1qrv20UrVQ%5#CPJLep->~jIeP0M1|#YaVgq5i{+BgLs}rOcQmhZjn#OZrf~9U9+6z-!ho<Z1&zp_8i*4xG$R5E=qyx~`bF`gA2th4b3CieII4=@9U7mlH|}`)QcaU8qv}#@^05_lq^NTIZO{L@gYVL=1}9&~ct{InwntGgD@U+lU`!B44x1F^fBUKSlylaA9GgMq+LI#sq51v~Hi`IArtJhD^#G;?>Hgb8H03Nd{mA;xjcZiGyrbb4*>5nUp?m=LJ!2vmVdkUOvZ($3<vM_7PiGCdWuB>DQk?1qX?2<pIiEPe%K3@@uS?t{P1!U8$Py-;-+hK*vWrA)>gr<;Q=@ANq!|5bWAaC#4Fet*nv<Ou7uUU=sG>of~J1`{{K#zp(OE07w>%JB!`*Lm~MbkP2^nd3}AuK|R{tdq`OD-jU6#<6FU{+0@3uSY%IXno7ul=TNfP0YB47)0{^w?_(Srd2wrRGt{DV`EPNWQUg)gp`;n|etp0ENopfwwH|)pDExW)3$r|)l+Z^O8%*DQRXg?HmYhpJV)H*5Q_A@OHL<LrFonwX!a~qrm0Ze^Ok)U`#l#VcLhIjt(D}#js86$@er$Jw-~D<cL@$P<qdhkZ2eZk;5llkR964Z*OEK##B_~y%zgL1QxFd+@hpv%V6m(Gf_GBU3^W^@W^9~Wne9h$aEd@sXSK9iDrZNR#s79%^uWCXv{!nUceYIAt2i&?Rpnv`^m|ATs+vGG4Cu=L-;EZ0T&p@OfZJ+|u`;703C<z%Lo$6=aGiiii?_9jKlJ#SPwUWX1;P_N%BN9Ji(I6X*?ldxV2Ok1UGah3xRz%qCfGgzP{!na-#fPgL1;a?8T&YNcF!+8^h-*EyVW`cd(cUav3nJ}-2m8@Ztk_UN?4fkBdR;B&zX_b1I5ZV8L%pZuzE1vU_cU*UG=0kF5&!sI_u`@V2|k~Dd0KGCX|p7Nhi1PAHrh6#!%?~j7kc<{#hZtJh~Nj1k&tw$u{YAg6@JXP=W@erR7@*dZBP#yJQ>jUFW^E+78q^a1XbEjBLMJ|l<^_lG<HQ?*Hbj{oN~w1Ar>Jqaw<E)XU#}N*d>Rl19%nQ&<F7*ai9$qnEt}mH}=k=?Fu$Rc&cW>o}LNb_xT@B-T374mf0c#w5kNP)`ihZTJhytnJT7P1qVAQXiBRM6Z@OrgH9{QGF^s@@PnkNGT;}OFCP7u+8(YhhxXE&3ivN}nL%J=TUK3Myae{8WmHI?L|wifW>Ss1+3Usq^XP*DS+uihqOKAa$OyMHGz7vk>YRh|XR&c(VUynXcLJ;msxNGT?%DtV<PSBpIOA%I00D#)hi?D?&an~TvBYQl0ssI200dcD")))

CANVAS_SQUARE_WIDTH = 8
CANVAS_SQUARE_HEIGHT = 9

LIGHT_SQUARE = "#DA5630"
DARK_SQUARE = "#A22200"

ASPECT_RATIO = CANVAS_SQUARE_WIDTH / CANVAS_SQUARE_HEIGHT
INIT_SCALE = 50

ENTRY_SIZE = 32

class InvalidAnnotatedBoardException(Exception):
    pass

class AnnotatedBoard:
    board: chess.Board
    eval: int
    wdl: int
    extra: int

    def __init__(self, packed: bytes):
        if len(packed) != ENTRY_SIZE:
            raise InvalidAnnotatedBoardException

        occupancy = int.from_bytes(packed[0:8], "little", signed=False)
        pieces = packed[8:8 + 16]
        [
            stm_ep_square,
            halfmove_clock,
            fullmove_number,
            eval,
            wdl,
            extra
        ] = struct.unpack_from("<BBHhBB", packed, 8 + 16)

        self.board = chess.Board(None)
        for i, square in enumerate(chess.scan_forward(occupancy)):
            encoded_piece = pieces[i // 2] >> (i % 2) * 4 & 0b1111

            color = encoded_piece >> 3
            if color not in range(2):
                raise InvalidAnnotatedBoardException
            color = chess.WHITE if color == 0 else chess.BLACK

            piece = encoded_piece & 0b111
            if piece not in range(7):
                raise InvalidAnnotatedBoardException
            if piece == 6:
                self.board.castling_rights |= 1 << square
                piece = chess.ROOK
            else:
                piece += 1
            
            self.board.set_piece_at(square, chess.Piece(piece, color))

        ep_square = stm_ep_square & 0b01111111
        if ep_square not in range(65):
            raise InvalidAnnotatedBoardException
        if ep_square == 64:
            ep_square = None
        self.board.ep_square = ep_square

        stm = stm_ep_square >> 7
        if stm not in range(2):
            raise InvalidAnnotatedBoardException
        stm = chess.WHITE if stm == 0 else chess.BLACK
        self.board.turn = stm

        self.board.halfmove_clock = halfmove_clock
        self.board.fullmove_number = fullmove_number

        self.eval = eval
        self.wdl = wdl
        self.extra = extra


class MarlinFormatViewer:
    dataset: Union[BufferedReader, None]
    position_index: int
    board: Union[AnnotatedBoard, None]
    root: Tk
    canvas: Canvas

    def __init__(self):
        self.dataset = None
        self.position_index = 0
        self.board = None

        self.root = Tk()
        self.root.geometry(f"{CANVAS_SQUARE_WIDTH * INIT_SCALE}x{CANVAS_SQUARE_HEIGHT * INIT_SCALE}")
        self.root.resizable(True, True)
        self.root.title("MarlinFormat viewer")
        self.root.bind("<Configure>", self.configure)
        self.root.bind("<KeyPress>", self.key_press)

        menu_bar = Menu(self.root)
        dataset_menu = Menu(menu_bar, tearoff=0)
        dataset_menu.add_command(label="Open new", command=self.open_new_pressed)
        dataset_menu.add_command(label="Validate")
        menu_bar.add_cascade(label="Dataset", menu=dataset_menu)
        menu_bar.add_command(label="Help", command=self.help_button_pressed)
        self.root.config(menu=menu_bar)

        self.canvas = Canvas(self.root)

    def configure(self, _event: Event):
        root_width = self.root.winfo_width()
        root_height = self.root.winfo_height()
        width = root_width
        height = int(root_width / ASPECT_RATIO)
        if height > root_height:
            height = root_height
            width = int(root_height * ASPECT_RATIO)
        x = (root_width - width) / 2
        y = (root_height - height) / 2
        self.canvas.place(x=x, y=y, width=width, height=height)
        self.repaint()

    def open_new_pressed(self):
        path = filedialog.askopenfilename()
        if path != "":
            if self.dataset is not None:
                self.dataset.close()
            self.dataset = open(path, "rb")
            self.board = None
            self.seek_to(0)
            self.repaint()

    def help_button_pressed(self):
        width = 400
        height = 150
        top = Toplevel(self.root)
        top.geometry(f"{width}x{height}")
        top.title("Help")
        Label(
            top,
            text="\n".join([
                "Dataset > Open new: Open new dataset",
                "Dataset > Validate: Ensure dataset contains only valid entries",
                "Left arrow or A: View previous entry",
                "Right arrow or D: View next entry",
            ]),
            font="TkDefaultFont"
        ).pack()
        

    def key_press(self, event: Event):
        if event.keysym in ("Left", "A") and self.position_index > 0:
            self.seek_to(self.position_index - 1)
            self.repaint()
        if event.keysym in ("Right", "D"):
            self.seek_to(self.position_index + 1)
            self.repaint()

    def seek_to(self, index: int):
        if self.dataset is None:
            return False
        self.dataset.seek(index * ENTRY_SIZE)
        entry = self.dataset.read(ENTRY_SIZE)
        try:
            self.board = AnnotatedBoard(entry)
        except InvalidAnnotatedBoardException:
            return False
        self.position_index = index
        return True

    def square_size(self):
        return self.canvas.winfo_width() / CANVAS_SQUARE_WIDTH

    def square_coords(self, square: chess.Square):
        file = chess.square_file(square)
        rank = chess.square_rank(square)
        x = file
        y = 7 - rank
        x2 = x + 1
        y2 = y + 1
        square_size = self.square_size()
        return x * square_size, y * square_size, x2 * square_size, y2 * square_size

    def draw_piece(self, piece: chess.PieceType, color: chess.Color, x, y, x2, y2):
        width = x2 - x
        height = y2 - y
        key = chess.piece_symbol(piece)
        if color == chess.WHITE:
            key = key.upper()
        segments = PIECE_DATA[key]
        for segment in segments:
            path = segment["path"]
            fill = segment["fill"]
            scaled_path = []
            for px, py in path:
                scaled_path.append(x + px * width)
                scaled_path.append(y + py * height)
            self.canvas.create_polygon(scaled_path, fill=fill, smooth=False)

    def repaint(self):
        self.canvas.delete("all")
        if self.board is not None:
            for square in chess.SQUARES:
                file = chess.square_file(square)
                rank = chess.square_rank(square)
                color = DARK_SQUARE if file % 2 == rank % 2 else LIGHT_SQUARE
                x, y, x2, y2 = self.square_coords(square)
                self.canvas.create_rectangle(x, y, x2, y2, fill=color, width=0)
            for square, piece in self.board.board.piece_map().items():
                x, y, x2, y2 = self.square_coords(square)
                self.draw_piece(piece.piece_type, piece.color, x, y, x2, y2)
            fill = "black"
            parts = []
            if not self.board.board.is_valid():
                fill = "darkred"
                parts.append("INVALID")
            parts.extend([
                f"{self.position_index}",
                f"{'White' if self.board.board.turn == chess.WHITE else 'Black'} to move",
                f"Eval: {self.board.eval}",
                f"WDL: {self.board.wdl}"
            ])
            
            self.canvas.create_text(
                self.canvas.winfo_width() / 2,
                8.5 * self.square_size(),
                fill=fill,
                font=("TkDefaultFont", int(self.square_size() / 4), "bold"),
                text=" | ".join(parts)
            )
        else:
            pass

    def mainloop(self):
        self.root.mainloop()

if __name__ == "__main__":
    MarlinFormatViewer().mainloop()
