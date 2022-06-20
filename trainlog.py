from typing import List


class TrainLog:
    def __init__(self, train_id: str):
        self.train_id = train_id
        self.losses: List[float] = []

    def update(self, loss: float):
        self.losses.append(loss)

    def save(self):
        logs = ""
        for epoch, loss in enumerate(self.losses):
            logs += f"epoch {epoch}: {loss}\n"
        with open(f"{self.train_id}.txt", "w") as log:
            log.write(logs)
