use serde::{Deserialize, Serialize};

use super::utils;

#[derive(Serialize, Deserialize)]
pub struct HalfKp {
    #[serde(rename = "ft.weight")]
    feature_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "ft.bias")]
    feature_bias: Box<[f32]>,
    #[serde(rename = "fft.weight")]
    v_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "fft.bias")]
    v_bias: Box<[f32]>,
    #[serde(rename = "out.weight")]
    out_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "out.bias")]
    out_bias: Box<[f32]>,
}

impl HalfKp {
    pub fn from(bytes: &[u8]) -> Self {
        serde_json::from_slice(bytes).unwrap()
    }

    pub fn to_bin(&self, ft_scale: f32, scale: f32) -> Vec<u8> {
        let mut bin = vec![];
        bin.extend((self.feature_weights.len() as u32).to_le_bytes());
        bin.extend((self.feature_weights[0].len() as u32).to_le_bytes());
        bin.extend((self.out_weights[0].len() as u32).to_le_bytes());

        let mut summed_weights = self.feature_weights.clone();
        let mut summed_bias = self.feature_bias.clone();

        let planes = summed_weights.len() / self.v_weights.len();
        println!("{}", planes);
        for i in 0..planes {
            for j in 0..self.v_weights.len() {
                assert_eq!(self.v_weights[0].len(), summed_weights[0].len());
                for k in 0..self.v_weights[0].len() {
                    summed_weights[self.v_weights.len() * i + j][k] += self.v_weights[j][k];
                }
            }
        }
        for (bias, v_bias) in summed_bias.iter_mut().zip(self.v_bias.iter()) {
            *bias += *v_bias
        }

        utils::serialize_dense_i16(&summed_weights, &mut bin, ft_scale);
        utils::serialize_flat_i16(&summed_bias, &mut bin, ft_scale);
        utils::serialize_dense_i8(&self.out_weights, &mut bin, scale);
        utils::serialize_flat_i16(&self.out_bias, &mut bin, ft_scale * scale);

        bin
    }
}
