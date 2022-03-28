import tensorflow as tf


class Dense(tf.keras.layers.Layer):
    def __init__(self, num_outputs):
        super(Dense, self).__init__()
        self.num_outputs = num_outputs

    def build(self, input_shape):
        self.kernel = self.add_weight(
            "kernel", shape=[int(input_shape[-1]), self.num_outputs]
        )
        self.bias = self.add_weight("bias", shape=[self.num_outputs])

    @tf.function
    def call(self, board):
        return tf.sparse.sparse_dense_matmul(board, self.kernel) + self.bias


class Factorize(tf.keras.Model):
    def __init__(self, ft_out: int):
        super(Factorize, self).__init__()
        self.model = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(640, sparse=True),
                Dense(ft_out),
            ]
        )

    @tf.function
    def call(self, board):
        return self.model(board)


class FeatureTransformer(tf.keras.Model):
    def __init__(self, ft_out: int):
        super(FeatureTransformer, self).__init__()
        self.model = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(40960, sparse=True),
                Dense(ft_out),
            ]
        )

    @tf.function
    def call(self, board):
        return self.model(board)


class NnBasic(tf.keras.Model):
    def __init__(self, ft_out: int):
        super(NnBasic, self).__init__()
        self.ft = FeatureTransformer(ft_out)
        self.fft = Factorize(ft_out)
        self.out = tf.keras.Sequential(
            [
                tf.keras.layers.InputLayer(ft_out * 2),
                tf.keras.layers.ReLU(max_value=1.0),
                tf.keras.layers.Dense(1),
            ]
        )

    @tf.function
    def call(self, boards):

        stm_ft = self.ft(boards[0])
        nstm_ft = self.ft(boards[1])

        f_stm_ft = self.fft(boards[2])
        f_nstm_ft = self.fft(boards[3])

        merge = tf.concat((stm_ft, nstm_ft), 1) + tf.concat((f_stm_ft, f_nstm_ft), 1)

        return tf.sigmoid(self.out(merge))
