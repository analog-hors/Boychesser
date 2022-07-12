from __future__ import annotations


class TrainLog:
    def __init__(self, train_id: str):
        self.train_id = train_id
        self.losses: list[float] = []

    def update(self, loss: float) -> None:
        self.losses.append(loss)

    def save(self) -> None:
        logs = ""
        for epoch, loss in enumerate(self.losses):
            logs += f"epoch {epoch}: {loss}\n"
        with open(f"runs/{self.train_id}.txt", "w") as log:
            log.write(logs)
