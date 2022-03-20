pub fn serialize_dense_i8(weights: &[Box<[f32]>], bin: &mut Vec<u8>, scale: f32) {
    for weights in weights {
        for &weight in weights.iter() {
            bin.extend(((weight * scale) as i8).to_le_bytes())
        }
    }
}

pub fn serialize_flat_i8(weights: &[f32], bin: &mut Vec<u8>, scale: f32) {
    for weight in weights.iter() {
        bin.extend(((*weight * scale) as i8).to_le_bytes())
    }
}

pub fn serialize_dense_i32(weights: &[Box<[f32]>], bin: &mut Vec<u8>, scale: f32) {
    for weights in weights {
        for &weight in weights.iter() {
            bin.extend(((weight * scale) as i32).to_le_bytes())
        }
    }
}

pub fn serialize_flat_i32(weights: &[f32], bin: &mut Vec<u8>, scale: f32) {
    for weight in weights.iter() {
        bin.extend(((*weight * scale) as i32).to_le_bytes())
    }
}
