pub struct Batch {
    // The maximum number of entries
    capacity: usize,

    features: Box<[SparseTensorList]>,
    indices_per_feature: usize,

    cp: Box<[f32]>,
    wdl: Box<[f32]>,
    buckets: Box<[i32]>,

    // The number of entries actually written
    entries: usize,
}

#[derive(Clone)]
struct SparseTensorList {
    feature_buffer: Box<[i64]>,
    values: Box<[f32]>,
    feature_count: usize,
}

impl SparseTensorList {
    fn new(capacity: usize, max_features: usize, indices_per_feature: usize) -> Self {
        Self {
            feature_buffer: vec![0; capacity * max_features * indices_per_feature]
                .into_boxed_slice(),
            values: vec![0.0; capacity * max_features].into_boxed_slice(),
            feature_count: 0,
        }
    }
}

impl Batch {
    pub fn new(
        capacity: usize,
        max_features: usize,
        indices_per_feature: usize,
        tensors_per_board: usize,
    ) -> Self {
        Self {
            capacity,
            features: vec![
                SparseTensorList::new(capacity, max_features, indices_per_feature);
                tensors_per_board
            ]
            .into_boxed_slice(),
            indices_per_feature,
            cp: vec![0_f32; capacity].into_boxed_slice(),
            wdl: vec![0_f32; capacity].into_boxed_slice(),
            buckets: vec![0; capacity].into_boxed_slice(),
            entries: 0,
        }
    }

    pub fn make_entry(&mut self, cp: f32, wdl: f32, bucket: i32) -> EntryFeatureWriter {
        let index_in_batch = self.entries;
        self.entries += 1;
        self.cp[index_in_batch] = cp;
        self.wdl[index_in_batch] = wdl;
        self.buckets[index_in_batch] = bucket;
        EntryFeatureWriter {
            batch: self,
            index_in_batch,
        }
    }

    pub fn clear(&mut self) {
        self.entries = 0;
        for list in &mut *self.features {
            list.feature_count = 0;
        }
    }

    pub fn capacity(&self) -> usize {
        self.capacity
    }

    pub fn len(&self) -> usize {
        self.entries
    }

    pub fn feature_buffer_ptr(&self, tensor: usize) -> *const i64 {
        self.features[tensor].feature_buffer.as_ptr()
    }

    pub fn feature_values_ptr(&self, tensor: usize) -> *const f32 {
        self.features[tensor].values.as_ptr()
    }

    pub fn feature_count(&self, tensor: usize) -> usize {
        self.features[tensor].feature_count
    }

    pub fn tensors_per_board(&self) -> usize {
        self.features.len()
    }

    pub fn indices_per_feature(&self) -> usize {
        self.indices_per_feature
    }

    pub fn cp_ptr(&self) -> *const f32 {
        &self.cp[0]
    }

    pub fn wdl_ptr(&self) -> *const f32 {
        &self.wdl[0]
    }

    pub fn bucket_ptr(&self) -> *const i32 {
        self.buckets.as_ptr()
    }
}

pub struct EntryFeatureWriter<'b> {
    batch: &'b mut Batch,
    index_in_batch: usize,
}

impl<'b> EntryFeatureWriter<'b> {
    pub fn add_feature(&mut self, tensor: usize, feature: i64, value: f32) {
        let list = &mut self.batch.features[tensor];
        let index = list.feature_count;
        list.feature_buffer[index * 2] = self.index_in_batch as i64;
        list.feature_buffer[index * 2 + 1] = feature;
        list.values[index] = value;
        list.feature_count += 1;
    }
}
