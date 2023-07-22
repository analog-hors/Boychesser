EXE = BoyChesser
ifeq ($(OS),Windows_NT)
	SRC := Chess-Challenge.exe
	DEST := $(EXE).exe
else
	SRC := Chess-Challenge
	DEST := $(EXE)
endif

all:
	dotnet publish -c Release --output Chess-Challenge/bin/ob_out
	mv Chess-Challenge/bin/ob_out/$(SRC) ./$(DEST)
