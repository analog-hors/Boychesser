use cozy_chess::Board;

pub trait BucketingScheme {
    const BUCKET_COUNT: usize;

    fn bucket(board: &Board) -> i32;
}

pub struct NoBucketing;

impl BucketingScheme for NoBucketing {
    const BUCKET_COUNT: usize = 1;

    fn bucket(_board: &Board) -> i32 {
        0
    }
}
