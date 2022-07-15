pub fn serialize_dense_i8(weights: &[Box<[f32]>], bin: &mut Vec<u8>, scale: f32) {
    for weights in weights {
        for &weight in weights.iter() {
            bin.extend(i8::try_from((weight * scale) as i64).unwrap().to_le_bytes())
        }
    }
}

pub fn serialize_flat_i8(weights: &[f32], bin: &mut Vec<u8>, scale: f32) {
    for &weight in weights {
        bin.extend(i8::try_from((weight * scale) as i64).unwrap().to_le_bytes())
    }
}

pub fn serialize_dense_i16(weights: &[Box<[f32]>], bin: &mut Vec<u8>, scale: f32) {
    for weights in weights {
        for &weight in weights.iter() {
            bin.extend(
                i16::try_from((weight * scale) as i64)
                    .unwrap()
                    .to_le_bytes(),
            )
        }
    }
}

pub fn serialize_flat_i16(weights: &[f32], bin: &mut Vec<u8>, scale: f32) {
    for &weight in weights {
        bin.extend(
            i16::try_from((weight * scale) as i64)
                .unwrap()
                .to_le_bytes(),
        )
    }
}
