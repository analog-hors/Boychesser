use serde::{Deserialize, Serialize};

use crate::utils;

#[derive(Serialize, Deserialize)]
pub struct StmArch {
    #[serde(rename = "dense/kernel:0")]
    feature_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "dense/bias:0")]
    feature_bias: Box<[f32]>,
    #[serde(rename = "dense_1/kernel:0")]
    out_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "dense_1/bias:0")]
    out_bias: Box<[f32]>, 
}

impl StmArch {
    pub fn from(bytes: &[u8]) -> Self {
        serde_json::from_slice(bytes).unwrap()
    }

    pub fn to_bin(&self, scale: f32) -> Vec<u8> {
        let mut bin = vec![];
        bin.extend((self.feature_weights.len() as u32).to_le_bytes());
        bin.extend((self.feature_weights[0].len() as u32).to_le_bytes());
        bin.extend((self.out_weights[0].len() as u32).to_le_bytes());

        utils::serialize_dense_i8(&self.feature_weights, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.feature_bias, &mut bin, 64.0);
        utils::serialize_dense_i8(&self.out_weights, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.out_bias, &mut bin, 64.0);

        bin
    }
}
