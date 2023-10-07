EXE = Boychesser
ifeq ($(OS),Windows_NT)
	SRC := Boychesser.Uci.exe
	DEST := $(EXE).exe
else
	SRC := Boychesser.Uci
	DEST := $(EXE)
endif

all:
	dotnet publish -c Release Boychesser.Uci/ --output Boychesser.Uci/bin/OpenbenchBin
	mv Boychesser.Uci/bin/OpenbenchBin/$(SRC) ./$(DEST)
