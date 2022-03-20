import tensorflow as tf


class FeatureTransformer(tf.keras.Model):
    def __init__(self, ft_out: int):
        super(FeatureTransformer, self).__init__()
        self.model = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(768),
                tf.keras.layers.Dense(ft_out),
                tf.keras.layers.ReLU(max_value=1.0),
            ]
        )

    def call(self, board):
        return self.model(board)


class NnBasic(tf.keras.Model):
    def __init__(self, ft_out: int):
        super(NnBasic, self).__init__()
        self.ft = FeatureTransformer(ft_out)
        self.out = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(ft_out * 2),
                tf.keras.layers.Dense(1),
            ]
        )

    def call(self, boards):
        stm_ft = self.ft(boards[0])
        nstm_ft = self.ft(boards[1])
        merge = tf.concat([stm_ft, nstm_ft], 1)
        return tf.sigmoid(self.out(merge))
