use serde::{Deserialize, Serialize};

use crate::utils;

#[derive(Serialize, Deserialize)]
pub struct BmArchV1 {
    #[serde(rename = "pair_mul.a.weight")]
    pair_mul_a: Box<[Box<[f32]>]>,
    #[serde(rename = "pair_mul.a.bias")]
    pair_mul_a_bias: Box<[f32]>,
    #[serde(rename = "pair_mul.b.weight")]
    pair_mul_b: Box<[Box<[f32]>]>,
    #[serde(rename = "pair_mul.b.bias")]
    pair_mul_b_bias: Box<[f32]>,
    #[serde(rename = "out.weight")]
    out_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "out.bias")]
    out_bias: Box<[f32]>,
    #[serde(rename = "res_t.weight")]
    res_weights: Box<[Box<[f32]>]>,
}

impl BmArchV1 {
    pub fn from(bytes: &[u8]) -> Self {
        serde_json::from_slice(bytes).unwrap()
    }

    pub fn to_bin(&self, scale: f32) -> Vec<u8> {
        let mut bin = vec![];
        bin.extend((self.pair_mul_a[0].len() as u32).to_le_bytes());
        bin.extend((self.out_weights[0].len() as u32).to_le_bytes());
        bin.extend((self.out_weights.len() as u32).to_le_bytes());

        utils::serialize_dense_i8(&self.pair_mul_a, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.pair_mul_a_bias, &mut bin, 64.0);
        utils::serialize_dense_i8(&self.pair_mul_b, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.pair_mul_b_bias, &mut bin, 64.0);
        utils::serialize_dense_i8(&self.out_weights, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.out_bias, &mut bin, 64.0);
        utils::serialize_dense_i32(&self.res_weights, &mut bin, 64.0 * scale);

        bin
    }
}

#[derive(Serialize, Deserialize)]
pub struct BmArchV2 {
    #[serde(rename = "feature_t.weight")]
    feature_t_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "feature_t.bias")]
    feature_t_bias: Box<[f32]>,
    #[serde(rename = "out.weight")]
    out_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "out.bias")]
    out_bias: Box<[f32]>,
    #[serde(rename = "s_out.weight")]
    s_out_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "s_out.bias")]
    s_out_bias: Box<[f32]>,
    #[serde(rename = "res_t.weight")]
    res_weights: Box<[Box<[f32]>]>,
    #[serde(rename = "s_res_t.weight")]
    s_res_weights: Box<[Box<[f32]>]>,
}

impl BmArchV2 {
    pub fn from(bytes: &[u8]) -> Self {
        serde_json::from_slice(bytes).unwrap()
    }

    pub fn to_bin(&self, scale: f32) -> Vec<u8> {
        let mut bin = vec![];
        bin.extend((self.feature_t_weights[0].len() as u32).to_le_bytes());
        bin.extend((self.out_weights[0].len() as u32).to_le_bytes());
        bin.extend((self.out_weights.len() as u32).to_le_bytes());

        utils::serialize_dense_i8(&self.feature_t_weights, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.feature_t_bias, &mut bin, 64.0);
        utils::serialize_dense_i8(&self.out_weights, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.out_bias, &mut bin, 64.0);
        utils::serialize_dense_i8(&self.s_out_weights, &mut bin, 64.0);
        utils::serialize_flat_i8(&self.s_out_bias, &mut bin, 64.0);
        utils::serialize_dense_i32(&self.res_weights, &mut bin, 64.0 * scale);
        utils::serialize_dense_i32(&self.s_res_weights, &mut bin, 64.0 * scale);

        bin
    }
}