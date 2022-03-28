pub fn serialize_dense_i8(weights: &[Box<[f32]>], bin: &mut Vec<u8>, scale: f32) {
    for weights in weights {
        for &weight in weights.iter() {
            assert!((weight * scale) <= 127.0 && (weight * scale) >= -128.0);
            bin.extend(((weight * scale) as i8).to_le_bytes())
        }
    }
}

pub fn serialize_flat_i8(weights: &[f32], bin: &mut Vec<u8>, scale: f32) {
    for weight in weights.iter() {
        assert!((weight * scale) <= 127.0 && (weight * scale) >= -128.0);
        bin.extend(((*weight * scale) as i8).to_le_bytes())
    }
}

pub fn serialize_dense_i16(weights: &[Box<[f32]>], bin: &mut Vec<u8>, scale: f32) {
    for weights in weights {
        for &weight in weights.iter() {
            assert!((weight * scale) <= 32767.0 && (weight * scale) >= -32768.0);
            bin.extend(((weight * scale) as i16).to_le_bytes())
        }
    }
}

pub fn serialize_flat_i16(weights: &[f32], bin: &mut Vec<u8>, scale: f32) {
    for weight in weights.iter() {
        assert!((weight * scale) <= 32767.0 && (weight * scale) >= -32768.0);
        bin.extend(((*weight * scale) as i16).to_le_bytes())
    }
}
